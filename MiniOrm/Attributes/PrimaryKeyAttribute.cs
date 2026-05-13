namespace MiniOrm.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class PrimaryKeyAttribute : Attribute
{
}
