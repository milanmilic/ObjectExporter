using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ObjectExporter
{
    public interface IObjectTraverser
    {
        Task<TraversalResult> TraverseAsync(object target, CancellationToken cancellationToken);
    }

    public class ObjectTraverser : IObjectTraverser
    {
        private readonly HashSet<object> _visitedObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);

        public async Task<TraversalResult> TraverseAsync(object target, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                var result = new TraversalResult();

                try
                {
                    TraverseObject(target, result.RootNode, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }

                return result;
            }, cancellationToken);
        }

        private void TraverseObject(object obj, Node node, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (obj == null)
            {
                node.Value = null;
                return;
            }

            if (_visitedObjects.Contains(obj))
            {
                node.Value = "[Circular Reference]";
                return;
            }

            _visitedObjects.Add(obj);

            try
            {
                var type = obj.GetType();

                if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime))
                {
                    node.Value = obj;
                }
                else if (obj is IEnumerable enumerable && !(obj is string))
                {
                    node.IsCollection = true;
                    var index = 0;

                    foreach (var item in enumerable)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var childNode = new Node();
                        TraverseObject(item, childNode, cancellationToken);
                        node.Children.Add($"[{index++}]", childNode);
                    }
                }
                else
                {
                    var properties = type.GetProperties();

                    foreach (var prop in properties)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!prop.CanRead)
                            continue;

                        try
                        {
                            var value = prop.GetValue(obj);
                            var childNode = new Node();
                            TraverseObject(value, childNode, cancellationToken);
                            node.Children.Add(prop.Name, childNode);
                        }
                        catch (Exception ex)
                        {
                            node.Children.Add(prop.Name, new Node { Value = $"[Error: {ex.Message}]" });
                        }
                    }
                }
            }
            finally
            {
                _visitedObjects.Remove(obj);
            }
        }
    }

    public class TraversalResult
    {
        public Node RootNode { get; set; } = new Node();
        public string Error { get; set; }
        public bool IsSuccessful
        {
            get { return string.IsNullOrEmpty(Error); }
        }
    }

    public class Node
    {
        public object Value { get; set; }
        public bool IsCollection { get; set; }
        public Dictionary<string, Node> Children { get; set; } = new Dictionary<string, Node>();
    }

    public class ReferenceEqualityComparer : EqualityComparer<object>
    {
        public new static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

        public override bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }

        public override int GetHashCode(object obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
