namespace BEngine.VisualScripting;

public sealed class VisualScriptVariableOverride
{
    public string name { get; set; } = string.Empty;
    public VisualScriptValue value { get; set; } = VisualScriptValue.Boolean(false);
}

public sealed class VisualScriptRunner : MonoBehaviour
{
    private sealed record Work(VisualScriptGraph Graph, string Node, int Depth);
    private sealed class WorkQueue
    {
        private readonly LinkedList<Work> _items = new();
        internal void Enqueue(Work work)
        {
            var item = _items.First;
            while (item is not null && item.Value.Depth >= work.Depth) item = item.Next;
            if (item is null) _items.AddLast(work); else _items.AddBefore(item, work);
        }
        internal Work Peek() => _items.First!.Value;
        internal bool TryPeek(out Work work) { work = _items.First?.Value!; return _items.Count > 0; }
        internal void Dequeue() => _items.RemoveFirst();
        internal void Clear() => _items.Clear();
    }
    private readonly WorkQueue _pending = new();
    private readonly Dictionary<string, VisualScriptValue> _variables = new(StringComparer.Ordinal);
    private bool _initialized;
    private bool _pauseAfterOne;
    private string? _skipBreakpoint;
    private string _eventName = string.Empty;
    private string _currentNodeId = string.Empty;
    private int _stepCount;
    private Fix64 _waitRemaining;
    private Work? _work;
    public string currentState { get; private set; } = string.Empty;

    public VisualScriptGraph? graph { get; set; }
    public List<VisualScriptVariableOverride> variableOverrides { get; set; } = [];
    public int maxStepsPerEvent { get; set; } = 4096;
    public VisualScriptExecutionStatus status { get; private set; } = VisualScriptExecutionStatus.Idle;
    public string lastError { get; private set; } = string.Empty;
    public IReadOnlyDictionary<string, VisualScriptValue> variables => _variables;
    public string currentNodeId => _currentNodeId;
    public string currentEventName => _eventName;
    public int executedStepCount => _stepCount;

    public override void Awake()
    {
        VisualScriptDebugHub.Register(this);
        InitializeVariables();
    }

    public override void Start() => RunEvent(VisualScriptNodeKind.StartEvent, "Start");

    public override void Update() => Advance(Time.deltaTime);
    public void Advance(Fix64 deltaTime)
    {
        if (status == VisualScriptExecutionStatus.Paused) return;
        if (status == VisualScriptExecutionStatus.Waiting)
        {
            _waitRemaining -= Fix64.Max(0, deltaTime);
            if (_waitRemaining > 0) return;
            status = VisualScriptExecutionStatus.Running; Pump(); return;
        }
        if (!RunEvent(VisualScriptNodeKind.UpdateEvent, "Update") || status == VisualScriptExecutionStatus.Completed)
            if (currentState.Length > 0) RunEvent(VisualScriptNodeKind.StateUpdateEvent, currentState);
    }

    public override void OnDestroy() => VisualScriptDebugHub.Unregister(this);

    public bool Trigger(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        return RunEvent(VisualScriptNodeKind.CustomEvent, eventName.Trim());
    }

    public bool TryGetVariable(string name, out VisualScriptValue value)
    {
        if (_variables.TryGetValue(name, out var found))
        {
            value = found.Clone();
            return true;
        }
        value = null!;
        return false;
    }

    public void SetVariable(string name, VisualScriptValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        EnsureInitialized();
        if (!_variables.TryGetValue(name, out var existing))
            throw new KeyNotFoundException($"Visual script variable '{name}' does not exist.");
        if (existing.kind != value.kind)
            throw new InvalidOperationException($"Variable '{name}' expects {existing.kind}, not {value.kind}.");
        _variables[name] = value.Clone();
        PublishState();
    }

    public bool ContinueExecution()
    {
        if (status != VisualScriptExecutionStatus.Paused) return false;
        _skipBreakpoint = _pending.TryPeek(out var next) ? next.Node : null;
        _pauseAfterOne = false;
        status = VisualScriptExecutionStatus.Running;
        Pump();
        return true;
    }

    public bool StepExecution()
    {
        if (status != VisualScriptExecutionStatus.Paused) return false;
        _skipBreakpoint = _pending.TryPeek(out var next) ? next.Node : null;
        _pauseAfterOne = true;
        status = VisualScriptExecutionStatus.Running;
        Pump();
        return true;
    }

    public void StopExecution()
    {
        _pending.Clear();
        _waitRemaining = 0;
        _currentNodeId = string.Empty;
        _eventName = string.Empty;
        status = VisualScriptExecutionStatus.Idle;
        PublishState();
    }

    public VisualScriptDebugSnapshot CreateDebugSnapshot() => new()
    {
        RunnerId = Id,
        RunnerName = gameObject.name,
        Status = status,
        EventName = _eventName,
        CurrentNodeId = _currentNodeId,
        StepCount = _stepCount,
        Error = lastError,
        Variables = _variables.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal)
    };

    private bool RunEvent(VisualScriptNodeKind kind, string eventName)
    {
        if (status is VisualScriptExecutionStatus.Paused or VisualScriptExecutionStatus.Running or VisualScriptExecutionStatus.Waiting) return false;
        if (graph is null) return false;
        try
        {
            EnsureInitialized();
            graph.Validate();
            var events = graph.nodes.Where(node => node.kind == kind &&
                (kind is not (VisualScriptNodeKind.CustomEvent or VisualScriptNodeKind.StateEnterEvent or VisualScriptNodeKind.StateUpdateEvent or VisualScriptNodeKind.StateExitEvent) ||
                 string.Equals(node.text, eventName, StringComparison.Ordinal))).ToArray();
            if (kind == VisualScriptNodeKind.StartEvent && currentState.Length > 0)
                events = events.Concat(graph.nodes.Where(node => node.kind == VisualScriptNodeKind.StateEnterEvent && node.text == currentState)).ToArray();
            if (events.Length == 0) return false;
            _pending.Clear();
            foreach (var node in events) _pending.Enqueue(new(graph, node.id, 0));
            _eventName = eventName;
            _currentNodeId = _pending.Peek().Node;
            _stepCount = 0;
            lastError = string.Empty;
            status = VisualScriptExecutionStatus.Running;
            Pump();
            return true;
        }
        catch (Exception exception)
        {
            Fault(exception.Message);
            return false;
        }
    }

    private void Pump()
    {
        try
        {
            while (_pending.TryPeek(out var work))
            {
                if (_stepCount >= Math.Max(1, maxStepsPerEvent))
                    throw new InvalidOperationException(
                        $"Visual script exceeded its {Math.Max(1, maxStepsPerEvent)} step limit during '{_eventName}'.");
                _work = work;
                var node = work.Graph.nodes.FirstOrDefault(item => item.id == work.Node) ??
                           throw new InvalidOperationException($"Queued visual script node '{work.Node}' no longer exists.");
                _currentNodeId = node.id;
                if (node.breakpoint && !string.Equals(_skipBreakpoint, node.id, StringComparison.Ordinal))
                {
                    status = VisualScriptExecutionStatus.Paused;
                    PublishState();
                    return;
                }
                if (string.Equals(_skipBreakpoint, node.id, StringComparison.Ordinal)) _skipBreakpoint = null;
                _pending.Dequeue();
                var output = Execute(node);
                _stepCount++;
                VisualScriptDebugHub.PublishTrace(new(Id, _eventName, node.id, node.kind, _stepCount,
                    DateTimeOffset.UtcNow));
                Enqueue(work, node, output);
                if (status == VisualScriptExecutionStatus.Waiting) { PublishState(); return; }
                if (_pauseAfterOne && _pending.TryPeek(out var pausedNode))
                {
                    _pauseAfterOne = false;
                    _currentNodeId = pausedNode.Node;
                    status = VisualScriptExecutionStatus.Paused;
                    PublishState();
                    return;
                }
            }
            _pauseAfterOne = false;
            _currentNodeId = string.Empty;
            status = VisualScriptExecutionStatus.Completed;
            PublishState();
        }
        catch (Exception exception)
        {
            Fault(exception.Message);
        }
    }

    private string? Execute(VisualScriptNode node)
    {
        switch (node.kind)
        {
            case VisualScriptNodeKind.StartEvent:
            case VisualScriptNodeKind.UpdateEvent:
            case VisualScriptNodeKind.CustomEvent:
            case VisualScriptNodeKind.StateEnterEvent:
            case VisualScriptNodeKind.StateUpdateEvent:
            case VisualScriptNodeKind.StateExitEvent:
                return "Out";
            case VisualScriptNodeKind.TransitionState:
                if (currentState == node.text) return "Out";
                Schedule(_work!.Graph, VisualScriptNodeKind.StateExitEvent, currentState, _work.Depth + 1);
                currentState = node.text;
                Schedule(_work.Graph, VisualScriptNodeKind.StateEnterEvent, currentState, _work.Depth + 1);
                return "Out";
            case VisualScriptNodeKind.CallSubGraph:
                if (_work!.Depth >= 16) throw new InvalidOperationException("Visual script exceeds 16 nested subgraphs.");
                var subGraph = node.graphPath.StartsWith("guid:", StringComparison.OrdinalIgnoreCase)
                    ? BAsset.LoadByGuid<VisualScriptGraph>(node.graphPath[5..]) : BAsset.Load<VisualScriptGraph>(node.graphPath);
                if (subGraph is null) throw new InvalidOperationException($"Subgraph '{node.graphPath}' could not be loaded.");
                subGraph.Validate();
                foreach (var variable in subGraph.variables)
                    if (!_variables.TryAdd(variable.name, variable.value.Clone()) && _variables[variable.name].kind != variable.value.kind)
                        throw new InvalidOperationException($"Subgraph variable '{variable.name}' conflicts with the caller's type.");
                Schedule(subGraph, node.text.Length == 0 ? VisualScriptNodeKind.StartEvent : VisualScriptNodeKind.CustomEvent, node.text, _work.Depth + 1);
                return "Out";
            case VisualScriptNodeKind.Wait:
                _waitRemaining = node.value.number;
                if (_waitRemaining > 0) status = VisualScriptExecutionStatus.Waiting;
                return "Out";
            case VisualScriptNodeKind.CustomAction:
                return VisualScriptActions.Execute(node.actionId, this, node);
            case VisualScriptNodeKind.MultiplyNumber:
                Require(node.variable, VisualScriptValueKind.Number).number *= node.value.number;
                return "Out";
            case VisualScriptNodeKind.DeltaTime:
                Assign(node.variable, VisualScriptValue.Number(Time.deltaTime)); return "Out";
            case VisualScriptNodeKind.KeyPressed:
                Assign(node.variable, VisualScriptValue.Boolean(Input.GetKeyDown(Enum.Parse<KeyCode>(node.text)))); return "Out";
            case VisualScriptNodeKind.SetActive:
                gameObject.SetActive(node.value.boolean); return "Out";
            case VisualScriptNodeKind.SetVariable:
                Assign(node.variable, node.value);
                return "Out";
            case VisualScriptNodeKind.AddNumber:
            {
                var current = Require(node.variable, VisualScriptValueKind.Number);
                current.number += node.value.number;
                return "Out";
            }
            case VisualScriptNodeKind.CompareNumber:
            {
                var left = Require(node.variable, VisualScriptValueKind.Number).number;
                var right = node.value.number;
                var result = node.comparison switch
                {
                    VisualScriptComparison.Equal => left == right,
                    VisualScriptComparison.NotEqual => left != right,
                    VisualScriptComparison.Less => left < right,
                    VisualScriptComparison.LessOrEqual => left <= right,
                    VisualScriptComparison.Greater => left > right,
                    VisualScriptComparison.GreaterOrEqual => left >= right,
                    _ => false
                };
                Assign(node.secondaryVariable, VisualScriptValue.Boolean(result));
                return "Out";
            }
            case VisualScriptNodeKind.Branch:
                return Require(node.variable, VisualScriptValueKind.Boolean).boolean ? "True" : "False";
            case VisualScriptNodeKind.SetPosition:
                transform.position = node.relative ? transform.position + node.value.vector : node.value.vector;
                return "Out";
            case VisualScriptNodeKind.Log:
                Debug.Log(Expand(node.text), this);
                return "Out";
            case VisualScriptNodeKind.Stop:
                _pending.Clear();
                return null;
            default:
                throw new InvalidOperationException($"Unsupported visual script node kind {node.kind}.");
        }
    }

    private void Schedule(VisualScriptGraph target, VisualScriptNodeKind kind, string name, int depth)
    {
        foreach (var node in target.nodes.Where(node => node.kind == kind && (kind == VisualScriptNodeKind.StartEvent || node.text == name)))
            _pending.Enqueue(new(target, node.id, depth));
    }
    private void Enqueue(Work work, VisualScriptNode node, string? output)
    {
        if (output is null) return;
        foreach (var edge in work.Graph.connections.Where(edge => edge.source == node.id && edge.sourcePort == output))
            _pending.Enqueue(new(work.Graph, edge.destination, work.Depth));
    }

    private VisualScriptValue Require(string name, VisualScriptValueKind kind)
    {
        if (!_variables.TryGetValue(name, out var value))
            throw new InvalidOperationException($"Visual script variable '{name}' was not initialized.");
        if (value.kind != kind)
            throw new InvalidOperationException($"Visual script variable '{name}' must be {kind}, but is {value.kind}.");
        return value;
    }

    private void Assign(string name, VisualScriptValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!_variables.TryGetValue(name, out var existing))
            throw new InvalidOperationException($"Visual script variable '{name}' was not initialized.");
        if (existing.kind != value.kind)
            throw new InvalidOperationException($"Visual script variable '{name}' expects {existing.kind}, not {value.kind}.");
        _variables[name] = value.Clone();
    }

    private string Expand(string text)
    {
        var result = text ?? string.Empty;
        foreach (var (name, value) in _variables)
            result = result.Replace("{" + name + "}", value.ToString(), StringComparison.Ordinal);
        return result;
    }

    private void InitializeVariables()
    {
        _variables.Clear();
        currentState = graph?.initialState ?? string.Empty;
        if (graph is not null)
        {
            graph.Validate();
            foreach (var variable in graph.variables) _variables.Add(variable.name, variable.value.Clone());
            foreach (var item in variableOverrides ?? [])
            {
                if (item is null || string.IsNullOrWhiteSpace(item.name) || item.value is null) continue;
                if (!_variables.TryGetValue(item.name, out var existing) || existing.kind != item.value.kind) continue;
                _variables[item.name] = item.value.Clone();
            }
        }
        _initialized = true;
        PublishState();
    }

    private void EnsureInitialized()
    {
        if (!_initialized) InitializeVariables();
    }

    private void Fault(string message)
    {
        _pending.Clear();
        _pauseAfterOne = false;
        status = VisualScriptExecutionStatus.Faulted;
        lastError = message;
        Debug.LogError($"Visual Script '{graph?.name ?? "<missing>"}' on '{gameObject.name}' failed: {message}", this);
        PublishState();
    }

    private void PublishState() => VisualScriptDebugHub.PublishState(CreateDebugSnapshot());
}
