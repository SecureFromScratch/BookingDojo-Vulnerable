namespace BookingDojo.Api.Authorization;

[AttributeUsage(AttributeTargets.Method)]
public sealed class OwnedResourceAttribute(Type resourceType) : Attribute
{
    public Type ResourceType { get; } = resourceType;
}
