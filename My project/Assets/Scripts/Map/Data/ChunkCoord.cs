using System;
using UnityEngine;

public struct ChunkCoord : IEquatable<ChunkCoord>
{
    public int x;
    public int y;

    public ChunkCoord(int x, int y) { this.x = x; this.y = y; }

    public bool Equals(ChunkCoord other) => x == other.x && y == other.y;
    public override bool Equals(object obj) => obj is ChunkCoord other && Equals(other);
    public override int GetHashCode() => (x * 73856093) ^ (y * 19349663);
    public override string ToString() => $"({x},{y})";

}
