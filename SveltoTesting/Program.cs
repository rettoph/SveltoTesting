using Svelto.ECS;
using Svelto.ECS.Schedulers;

var simpleEntitiesSubmissionScheduler = new SimpleEntitiesSubmissionScheduler();
var enginesRoot = new EnginesRoot(simpleEntitiesSubmissionScheduler);

var factory = enginesRoot.GenerateEntityFactory();
var functions = enginesRoot.GenerateEntityFunctions();

var engine = new NodeEngine(functions, factory);
enginesRoot.AddEngine(engine);

for (int i = 0; i < 100; i++)
{
    Console.WriteLine("Loop: " + i);

    engine.CreateTree();
    engine.CreateTree();
    engine.CreateTree();

    simpleEntitiesSubmissionScheduler.SubmitEntities();

    engine.IterateAllTreesAndTheirNodesThenClearFilter();
    engine.DeleteAllEntities();
}

public struct Node : IEntityComponent
{
    private static int CurrentFilterId;

    public static readonly int TreeFilterId = CurrentFilterId++;
    public static readonly FilterContextID ChildrenFilterContextId = FilterContextID.GetNewContextID();
    public static readonly FilterContextID TreeFilterContextId = FilterContextID.GetNewContextID();

    public readonly EGID? Parent;
    public readonly int ChildrenFilterId;

    public Node(EGID? parent)
    {
        this.Parent = parent;
        this.ChildrenFilterId = CurrentFilterId++;
    }
}

class NodeDescriptor : IEntityDescriptor
{
    public IComponentBuilder[] componentsToBuild { get; } = new IComponentBuilder[]
    {
        new ComponentBuilder<Node>()
    };
}

public class NodeEngine : IQueryingEntitiesEngine, IReactOnAddEx<Node>
{
    private ExclusiveGroup _group = new ExclusiveGroup();
    private uint _currentEntityId;
    private IEntityFunctions _functions;
    private IEntityFactory _factory;

    public EntitiesDB entitiesDB { get; set; } = null!;

    public NodeEngine(IEntityFunctions functions, IEntityFactory factory)
    {
        _functions = functions;
        _factory = factory;
    }

    public void Ready() { }

    public void DeleteAllEntities()
    {
        _functions.RemoveEntitiesFromGroup(_group);
    }

    public void IterateAllTreesAndTheirNodesThenClearFilter()
    {
        // Get all trees
        ref var treesFilter = ref this.entitiesDB.GetFilters().GetOrCreatePersistentFilter<Node>(Node.TreeFilterId, Node.TreeFilterContextId);
        foreach (var (indices, groupId) in treesFilter)
        {
            var (nodes, _) = this.entitiesDB.QueryEntities<Node>(groupId);

            for (int i = 0; i < indices.count; i++)
            {
                uint index = indices[i];
                ref Node head = ref nodes[index];
                this.IterateAllChildNodesRecersive(ref head); // Recersively iterate through all nodes in the tree
            }
        }

        // Attempt to access the original treesFilter
        var count = treesFilter.ComputeFinalCount();
        treesFilter.Clear();
    }

    public void IterateAllChildNodesRecersive(ref Node node)
    {
        ref var childrenFilter = ref this.entitiesDB.GetFilters().GetOrCreatePersistentFilter<Node>(node.ChildrenFilterId, Node.ChildrenFilterContextId);
        foreach (var (indices, groupId) in childrenFilter)
        {
            var (nodes, _) = this.entitiesDB.QueryEntities<Node>(groupId);

            for (int i = 0; i < indices.count; i++)
            {
                uint index = indices[i];
                this.IterateAllChildNodesRecersive(ref nodes[index]);
            }
        }
    }

    public EGID CreateNode(EGID? parent = default)
    {
        var node = new Node(parent);

        var initializer = _factory.BuildEntity<NodeDescriptor>(_currentEntityId++, _group);
        initializer.Init(node);

        return initializer.EGID;
    }

    public EGID CreateTree()
    {
        var head = this.CreateNode();

        for (int l1 = 0; l1 < 5; l1++)
        {
            var n1 = CreateNode(head);

            for (int l2 = 0; l2 < 5; l2++)
            {
                var n2 = CreateNode(n1);

                for (int l3 = 0; l3 < 5; l3++)
                {
                    var n3 = CreateNode(n2);
                }
            }
        }

        return head;
    }

    /// <summary>
    /// Add nodes either to the tree filter or the parent node's children filter
    /// </summary>
    /// <param name="rangeOfEntities"></param>
    /// <param name="entities"></param>
    /// <param name="groupID"></param>
    public void Add((uint start, uint end) rangeOfEntities, in EntityCollection<Node> entities, ExclusiveGroupStruct groupID)
    {
        var (nodes, ids, _) = entities;
        for (uint i = rangeOfEntities.start; i < rangeOfEntities.end; i++)
        {
            var node = nodes[i];

            if (node.Parent.HasValue)
            {
                var parent = this.entitiesDB.QueryEntity<Node>(node.Parent.Value);
                ref var childrenFilters = ref this.entitiesDB.GetFilters().GetOrCreatePersistentFilter<Node>(parent.ChildrenFilterId, Node.ChildrenFilterContextId);

                childrenFilters.Add(ids[i], groupID, i);
            }
            else
            {
                ref var treeFilter = ref this.entitiesDB.GetFilters().GetOrCreatePersistentFilter<Node>(Node.TreeFilterId, Node.TreeFilterContextId);
                treeFilter.Add(ids[i], groupID, i);
            }
        }
    }
}