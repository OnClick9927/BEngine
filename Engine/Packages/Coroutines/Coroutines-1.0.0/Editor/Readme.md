# Coroutines package

Coroutine scheduling is provided by the `com.bengine.coroutines` package. Enable
it from Package Manager. Scheduling, reflection dispatch and yield instructions
are implemented in `BEngine.Coroutines`, outside Core.

The public types keep the `BEngine` namespace. Coroutine and delayed invocation
methods are extension methods on `MonoBehaviour`: use `this.StartCoroutine(Run())`,
`this.Invoke(nameof(Tick), 1)`, `this.StopAllCoroutines()` and `this.CancelInvoke()`
inside a behaviour. Calls through an explicit component variable keep their syntax.
Old unqualified calls such as `StartCoroutine(Run())` must add `this.`.

The package registers a scene runtime system. Work is canceled and iterators are
disposed when the owner is destroyed or the scene runtime stops.
