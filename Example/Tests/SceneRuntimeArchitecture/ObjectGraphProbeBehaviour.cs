namespace BEngine.ExampleTests.SceneRuntimeArchitecture;

internal sealed class ObjectGraphProbeBehaviour : MonoBehaviour
{
    [SerializeField] private int _privateValue;
    [SerializeReference] public ProbeNode? Polymorphic;
    public int[] Values = [];
    public int[,] Grid = new int[0, 0];
    public List<ProbeNode> Nodes = [];
    public Dictionary<string, ProbeNode> Lookup = [];
    public ProbeNode? SharedA;
    public ProbeNode? SharedB;
    public GameObject? TargetObject;
    public SceneProbeBehaviour? TargetComponent;
    public int? OptionalValue;
    public ProbeMode Mode;

    public int PrivateValue => _privateValue;

    internal void SetPrivateValue(int value) => _privateValue = value;

    internal enum ProbeMode : int
    {
        Negative = -2,
        Positive = 3
    }

    internal class ProbeNode
    {
        public string Name = string.Empty;
        public ProbeNode? Next;
    }

    internal sealed class DerivedProbeNode : ProbeNode
    {
        public int Weight;
    }
}
