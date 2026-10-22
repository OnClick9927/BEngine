using System;
using BEngine;

namespace BEngine.Examples.Pooling;

// Attach to a GameObject and enter Play. Output appears in the Console.
public sealed class PoolingExample : MonoBehaviour
{
    public override void Start()
    {
        using var lists = new ListPool<int>(maxSize: 4, maxActive: 2, maxRetainedCapacity: 64);
        lists.Prewarm(3);
        using (var rental = lists.Rent())
        {
            rental.Value.Add(42);
            Debug.Log("Pooled list: " + rental.Value[0]);
        }
        using (var rental = lists.Rent())
            Debug.Log("Reused list is empty: " + (rental.Value.Count == 0));
        lists.Trim(1);
        Debug.Log("Pooling statistics: " + lists.Statistics);
    }
}
