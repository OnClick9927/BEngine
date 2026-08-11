namespace BEngine;

public sealed class Scene : BObject
{
    private readonly List<GameObject> _gameObjects = [];

    public IReadOnlyList<GameObject> gameObjects => _gameObjects;
    public IEnumerable<GameObject> rootGameObjects => _gameObjects.Where(item => item.transform.parent is null);

    public Scene(string name = "Untitled") => this.name = name;

    public GameObject CreateGameObject(string name = "GameObject")
    {
        var gameObject = new GameObject(name);
        Add(gameObject);
        return gameObject;
    }

    internal void Add(GameObject gameObject)
    {
        if (gameObject.scene is not null)
        {
            throw new InvalidOperationException("GameObject already belongs to a Scene.");
        }

        gameObject.scene = this;
        _gameObjects.Add(gameObject);
    }

    public GameObject? Find(string name) => _gameObjects.FirstOrDefault(item => item.name == name);
    public GameObject? Find(Guid id) => _gameObjects.FirstOrDefault(item => item.Id == id);

    public bool Destroy(GameObject gameObject)
    {
        if (!_gameObjects.Contains(gameObject))
        {
            return false;
        }

        foreach (var child in gameObject.transform.children.ToArray())
        {
            Destroy(child.gameObject);
        }

        foreach (var behaviour in gameObject.GetComponents<MonoBehaviour>())
        {
            behaviour.OnDestroy();
        }

        gameObject.transform.SetParent(null, false);
        gameObject.scene = null;
        return _gameObjects.Remove(gameObject);
    }
}
