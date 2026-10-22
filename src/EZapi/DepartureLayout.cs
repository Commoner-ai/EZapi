namespace EZapi;

// Native-parent coordinates, not screen pixels. No Unity objects escape this planner.
internal readonly record struct DepartureRect(float X, float Y, float Width, float Height)
{
    internal float Right => X + Width;
    internal float Top => Y + Height;
    internal float CenterX => X + Width / 2;
    internal float CenterY => Y + Height / 2;
    internal bool Valid => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Right) && float.IsFinite(Top) && Width >= 1 && Height >= 1;
    internal DepartureRect Expand(float padding) => new(X - padding, Y - padding, Width + 2 * padding, Height + 2 * padding);
    internal bool Contains(DepartureRect other) => other.X >= X && other.Y >= Y && other.Right <= Right && other.Top <= Top;
    internal bool Overlaps(DepartureRect other) => X < other.Right && Right > other.X && Y < other.Top && Top > other.Y;
}

internal static class DepartureLayout
{
    // Retains DeepSpace's first choices: native left/middle/right columns below the
    // existing row. An additional bounded scan uses remaining visible space.
    internal static IReadOnlyList<DepartureRect?> Plan(DepartureRect visible, DepartureRect origin,
        float leftX, float gapX, float gapY, IReadOnlyList<DepartureRect> obstacles, int count, bool reserveLegacy)
    {
        var result = new DepartureRect?[Math.Clamp(count, 0, 64)];
        if (!visible.Valid || !origin.Valid || visible.Width < origin.Width || visible.Height < origin.Height || !float.IsFinite(leftX) || !float.IsFinite(gapX) || !float.IsFinite(gapY) || gapX < 0 || gapY < 0 || obstacles.Count > 4096)
            return result;
        float dx = origin.Width + gapX, dy = origin.Height + gapY;
        var occupied = obstacles.Where(b => b.Valid).Select(b => b.Expand(3)).ToList();
        if (reserveLegacy)
            for (int row = 1; row <= 4; row++) occupied.Add(new DepartureRect(origin.X, origin.Y - row * dy, origin.Width, origin.Height).Expand(3));
        var candidates = new List<DepartureRect>();
        void Candidate(float x, float y)
        {
            var rect = new DepartureRect(x - origin.Width / 2, y - origin.Height / 2, origin.Width, origin.Height);
            if (visible.Contains(rect) && !candidates.Contains(rect)) candidates.Add(rect);
        }
        foreach (int row in Enumerable.Range(1, 10))
            foreach (float x in new[] { leftX, (leftX + origin.CenterX) / 2, origin.CenterX }) Candidate(x, origin.CenterY - row * dy);
        // At most4096 probes total: malformed transforms cannot create an unbounded loop.
        int probes = 0;
        for (float y = visible.Top - origin.Height / 2; y - origin.Height / 2 >= visible.Y && probes < 4096; y -= dy)
        {
            for (float x = visible.X + origin.Width / 2; x + origin.Width / 2 <= visible.Right && probes < 4096; x += dx)
            {
                Candidate(x, y); probes++;
                if (x + dx == x) break;
            }
            if (y - dy == y) break;
        }
        int cursor = 0;
        for (int i = 0; i < result.Length; i++)
            while (cursor < candidates.Count)
            {
                var candidate = candidates[cursor++];
                if (occupied.Any(b => b.Overlaps(candidate))) continue;
                result[i] = candidate; occupied.Add(candidate.Expand(3)); break;
            }
        return result;
    }
}
