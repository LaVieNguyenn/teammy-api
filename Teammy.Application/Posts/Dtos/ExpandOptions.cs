namespace Teammy.Application.Posts.Dtos;

[System.Flags]
public enum ExpandOptions
{
    None = 0,
    Semester = 1 << 0,
    Group = 1 << 1,
    Major = 1 << 2,
    User = 1 << 3
}

