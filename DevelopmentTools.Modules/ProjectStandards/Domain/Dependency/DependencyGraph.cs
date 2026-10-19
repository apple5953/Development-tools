using System.Collections.Generic;
using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Dependency
{
    public class DependencyGraph
    {
        public Dictionary<StableIdentity, DependencyNode> Nodes { get; set; }

        public DependencyGraph()
        {
            Nodes = new Dictionary<StableIdentity, DependencyNode>();
        }

        public void AddNode(DependencyNode node)
        {
            if (node != null && !Nodes.ContainsKey(node.Identity))
            {
                Nodes[node.Identity] = node;
            }
        }

        public void AddDependency(StableIdentity dependent, StableIdentity provider)
        {
            DependencyNode depNode;
            DependencyNode provNode;
            if (Nodes.TryGetValue(dependent, out depNode) && Nodes.TryGetValue(provider, out provNode))
            {
                depNode.DependsOn.Add(provider);
                provNode.RequiredBy.Add(dependent);
            }
        }
    }
}
