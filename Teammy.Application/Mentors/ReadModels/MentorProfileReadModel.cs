using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Teammy.Application.Mentors.ReadModels
{
    public sealed record MentorProfileReadModel(
      Guid Id,
      string DisplayName,
      string Email,
      IReadOnlyList<string> Skills,
      string? Bio,
      IReadOnlyList<object> Availability
  );
}
