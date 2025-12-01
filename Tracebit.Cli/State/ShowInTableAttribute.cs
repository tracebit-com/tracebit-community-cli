namespace Tracebit.Cli.State;

[AttributeUsage(AttributeTargets.Property)]
public class ShowInTableAttribute(bool show, string? columnName) : Attribute
{
    public readonly bool Show = show;
    public readonly string? ColumnName = columnName;

    public ShowInTableAttribute(bool show) : this(show, null) { }

    public ShowInTableAttribute(string columnName) : this(true, columnName) { }
}
