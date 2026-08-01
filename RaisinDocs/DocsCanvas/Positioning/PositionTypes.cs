namespace RaisinDocs;

/// <summary>
/// Represents an absolute screen X coordinate (pixels from left edge of canvas).
/// Used for layout calculations and rendering positions.
/// </summary>
public readonly struct AbsoluteX
{
    public double Value { get; }

    public AbsoluteX(double value)
    {
        Value = value;
    }

    public static implicit operator double(AbsoluteX x) => x.Value;
    public static explicit operator AbsoluteX(double x) => new(x);

    public override string ToString() => $"AbsoluteX({Value})";
}

/// <summary>
/// Represents a relative X offset (width or distance, not a screen position).
/// Used for calculating how much horizontal space an element consumes.
/// </summary>
public readonly struct RelativeX
{
    public double Value { get; }

    public RelativeX(double value)
    {
        Value = value;
    }

    public static implicit operator double(RelativeX x) => x.Value;
    public static explicit operator RelativeX(double x) => new(x);

    public override string ToString() => $"RelativeX({Value})";
}

/// <summary>
/// Represents an absolute screen Y coordinate (pixels from top edge of canvas).
/// Used for layout calculations and rendering positions.
/// </summary>
public readonly struct AbsoluteY
{
    public double Value { get; }

    public AbsoluteY(double value)
    {
        Value = value;
    }

    public static implicit operator double(AbsoluteY y) => y.Value;
    public static explicit operator AbsoluteY(double y) => new(y);

    public override string ToString() => $"AbsoluteY({Value})";
}

/// <summary>
/// Represents a relative Y offset (height or distance, not a screen position).
/// Used for calculating how much vertical space an element consumes.
/// </summary>
public readonly struct RelativeY
{
    public double Value { get; }

    public RelativeY(double value)
    {
        Value = value;
    }

    public static implicit operator double(RelativeY y) => y.Value;
    public static explicit operator RelativeY(double y) => new(y);

    public override string ToString() => $"RelativeY({Value})";
}
