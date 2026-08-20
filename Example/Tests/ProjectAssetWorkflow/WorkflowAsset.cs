namespace BEngine.ExampleTests.ProjectAssetWorkflow;

[CreateAssetMenu(fileName = "New Workflow Asset", menuName = "Testing/Workflow Asset", order = 17)]
internal sealed class WorkflowAsset : ScriptableObject
{
    public string description { get; set; } = "Project CreateAssetMenu probe";
}
