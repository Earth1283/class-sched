namespace ClassSchedule.Core;

/// <summary>Side-by-side lane assignment for classes that overlap in time.</summary>
public static class OverlapLayout
{
    public readonly record struct Slot(int Lane, int Lanes);

    /// <summary>
    /// Returns one slot per input item, in input order. Items that overlap (directly or through a chain)
    /// share a cluster; each cluster is split into as many lanes as it needs.
    /// </summary>
    public static Slot[] Assign(IReadOnlyList<(double Start, double End)> items)
    {
        var result = new Slot[items.Count];
        var order = Enumerable.Range(0, items.Count)
            .OrderBy(i => items[i].Start).ThenBy(i => items[i].End).ToList();

        var cluster = new List<int>();
        var laneEnds = new List<double>();
        var lanes = new int[items.Count];
        var clusterEnd = double.MinValue;

        void Flush()
        {
            foreach (var i in cluster) result[i] = new Slot(lanes[i], laneEnds.Count);
            cluster.Clear();
            laneEnds.Clear();
            clusterEnd = double.MinValue;
        }

        foreach (var i in order)
        {
            var (start, end) = items[i];
            if (cluster.Count > 0 && start >= clusterEnd) Flush();

            var lane = laneEnds.FindIndex(e => e <= start);
            if (lane < 0)
            {
                laneEnds.Add(end);
                lane = laneEnds.Count - 1;
            }
            else laneEnds[lane] = end;

            lanes[i] = lane;
            cluster.Add(i);
            clusterEnd = Math.Max(clusterEnd, end);
        }
        Flush();
        return result;
    }
}
