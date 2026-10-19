using System;
using System.Collections.Generic;
using System.Linq;
using DevelopmentTools.ProjectStandards.Domain.Diff;
using DevelopmentTools.ProjectStandards.Domain.Identity;
using DevelopmentTools.ProjectStandards.Domain.Models;

namespace DevelopmentTools.ProjectStandards.Domain.Dependency
{
    public class DependencyResolver : IDependencyResolver
    {
        public DependencyGraph Build(IEnumerable<DiffItem> selectedItems, IEnumerable<DependencyReference> knownDependencies)
        {
            var graph = new DependencyGraph();

            if (selectedItems != null)
            {
                foreach (var item in selectedItems)
                {
                    var identity = item.MasterIdentity ?? item.TargetIdentity;
                    if (identity == null) continue;

                    var node = new DependencyNode();
                    node.Identity = identity;
                    node.AdapterKey = item.AdapterKey;
                    node.DiffItem = item;
                    node.Operation = new SyncOperation
                    {
                        AdapterKey = item.AdapterKey,
                        Action = item.SuggestedAction,
                        Source = item.MasterIdentity,
                        Target = item.TargetIdentity,
                        Risk = item.Risk,
                        Selected = true,
                        Description = string.Format("{0} {1}", item.SuggestedAction, identity.DisplayName)
                    };

                    graph.AddNode(node);
                }
            }

            if (knownDependencies != null)
            {
                foreach (var dep in knownDependencies)
                {
                    if (dep.SourceIdentity != null && dep.DependsOnIdentity != null)
                    {
                        graph.AddDependency(dep.SourceIdentity, dep.DependsOnIdentity);
                    }
                }
            }

            return graph;
        }

        public IReadOnlyList<SyncOperation> Sort(DependencyGraph graph)
        {
            if (graph == null || graph.Nodes.Count == 0)
            {
                return new List<SyncOperation>();
            }

            var inDegree = new Dictionary<StableIdentity, int>();
            foreach (var kvp in graph.Nodes)
            {
                int validDeps = kvp.Value.DependsOn.Count(dep => graph.Nodes.ContainsKey(dep));
                inDegree[kvp.Key] = validDeps;
            }

            var queue = new Queue<DependencyNode>(graph.Nodes.Values.Where(n => inDegree[n.Identity] == 0));
            var sortedOperations = new List<SyncOperation>();
            var visited = new HashSet<StableIdentity>();

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (visited.Contains(current.Identity)) continue;

                visited.Add(current.Identity);
                if (current.Operation != null)
                {
                    sortedOperations.Add(current.Operation);
                }

                foreach (var dependentIdentity in current.RequiredBy)
                {
                    DependencyNode dependentNode;
                    if (graph.Nodes.TryGetValue(dependentIdentity, out dependentNode))
                    {
                        inDegree[dependentIdentity]--;
                        if (inDegree[dependentIdentity] == 0)
                        {
                            queue.Enqueue(dependentNode);
                        }
                    }
                }
            }

            if (visited.Count < graph.Nodes.Count)
            {
                foreach (var remainingNode in graph.Nodes.Values)
                {
                    if (!visited.Contains(remainingNode.Identity))
                    {
                        var op = remainingNode.Operation ?? new SyncOperation
                        {
                            AdapterKey = remainingNode.AdapterKey,
                            Source = remainingNode.Identity,
                            Selected = false
                        };
                        op.IsBlocked = true;
                        op.BlockReason = "Circular dependency detected or unresolvable dependency order";
                        sortedOperations.Add(op);
                    }
                }
            }

            return sortedOperations;
        }
    }
}
