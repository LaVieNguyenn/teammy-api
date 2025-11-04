namespace Teammy.Domain.Users;

public sealed class Role
{
    public Guid Id { get; }
    public string Name { get; }  // admin / moderator / mentor / student

    public Role(Guid id, string name) { Id = id; Name = name; }
}
