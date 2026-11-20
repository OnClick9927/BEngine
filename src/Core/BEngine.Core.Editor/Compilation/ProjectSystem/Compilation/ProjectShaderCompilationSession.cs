namespace BEngine.ProjectSystem.Editor;

/// <summary>Owns one project's shader compilation requests. Prepare, cancel, apply and
/// dispose on the creating thread; build a request on a worker. Does not schedule tasks,
/// select shader formats, write artifacts or trigger scene/UI refresh.</summary>
public sealed class ProjectShaderCompilationSession : IDisposable
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly object _stateGate = new();
    // No wait handle is exposed. Keep this managed gate alive for retiring workers.
    private readonly SemaphoreSlim _buildGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private ProjectWorkspace? _workspace;
    private long _revision;
    private bool _disposed;

    public ProjectShaderCompilationSession(ProjectWorkspace workspace) =>
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

    /// <summary>Snapshots the inputs and compiler identity, invalidating older results.
    /// Invalidating a request does not cancel its caller's token or abort extension code.</summary>
    public CompilationRequest PrepareCompilation(IEnumerable<string> assetPaths)
    {
        VerifyOwner();
        ArgumentNullException.ThrowIfNull(assetPaths);
        var paths = assetPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Shader inputs must contain nonempty paths.", nameof(assetPaths));
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new CompilationRequest(this, ++_revision, paths, ProjectShaderCompiler.CaptureRegistration());
        }
    }

    /// <summary>Invalidates outstanding requests/results. The host owns replacement scheduling.</summary>
    public void CancelPending()
    {
        VerifyOwner();
        lock (_stateGate) _revision++;
    }

    /// <summary>Applies a result at most once on the owner thread. Returns false for an
    /// obsolete, cancelled, consumed or retired result. Foreign results are rejected.</summary>
    public bool TryApply(CompilationResult result)
    {
        VerifyThread();
        ArgumentNullException.ThrowIfNull(result);
        if (!ReferenceEquals(result.Owner, this))
            throw new ArgumentException("The shader result belongs to another session.", nameof(result));
        lock (_stateGate)
        {
            if (_disposed || result.Revision != _revision || result.CancellationToken.IsCancellationRequested ||
                Interlocked.Exchange(ref result.Consumed, 1) != 0) return false;
            return ProjectShaderCompiler.TryApplyCompilationResult(result.Artifacts);
        }
    }

    public void Dispose()
    {
        VerifyThread();
        lock (_stateGate)
        {
            if (_disposed) return;
            _disposed = true;
            _revision++;
            _workspace = null;
        }
        // Never wait for an extension on the editor thread. Linked tokens wake queued
        // builds; executing extensions retain their own local inputs until they return.
        try { _lifetime.Cancel(); }
        finally { _lifetime.Dispose(); }
    }

    private CompilationResult Build(CompilationRequest request, CancellationToken cancellationToken)
    {
        ProjectWorkspace workspace;
        CancellationTokenSource linked;
        lock (_stateGate)
        {
            Validate(request.Revision);
            workspace = _workspace!;
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        }
        using (linked)
        {
            _buildGate.Wait(linked.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                lock (_stateGate) Validate(request.Revision);
                var artifacts = ProjectShaderCompiler.CompileCaptured(workspace, request.Paths,
                    request.Registration, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                return new CompilationResult(this, request.Revision, artifacts, cancellationToken);
            }
            finally { _buildGate.Release(); }
        }
    }

    private void Validate(long revision)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (revision != _revision) throw new InvalidOperationException("This shader request is obsolete.");
    }

    private void VerifyThread()
    {
        if (_ownerThread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("This operation must run on the shader session's owner thread.");
    }

    private void VerifyOwner()
    {
        VerifyThread();
        lock (_stateGate) ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public sealed class CompilationRequest
    {
        internal readonly ProjectShaderCompilationSession Owner;
        internal readonly long Revision;
        internal readonly string[] Paths;
        internal readonly object? Registration;
        private int _started;
        internal CompilationRequest(ProjectShaderCompilationSession owner, long revision, string[] paths, object? registration)
        { Owner = owner; Revision = revision; Paths = paths; Registration = registration; }

        /// <summary>Builds once, serially with this session's other builds, without applying changes.</summary>
        public CompilationResult Build(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _started, 1) != 0)
                throw new InvalidOperationException("This shader request has already been built.");
            return Owner.Build(this, cancellationToken);
        }
    }

    public sealed class CompilationResult
    {
        internal readonly ProjectShaderCompilationSession Owner;
        internal readonly long Revision;
        internal readonly CancellationToken CancellationToken;
        internal int Consumed;
        internal CompilationResult(ProjectShaderCompilationSession owner, long revision,
            ShaderCompilationResult artifacts, CancellationToken cancellationToken)
        { Owner = owner; Revision = revision; Artifacts = artifacts; CancellationToken = cancellationToken; }
        public ShaderCompilationResult Artifacts { get; }
    }
}
