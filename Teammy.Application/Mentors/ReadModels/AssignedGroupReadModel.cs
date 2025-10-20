using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Teammy.Application.Mentors.ReadModels
{
    public sealed record AssignedGroupReadModel(
      Guid GroupId,
      string Name,
      string Status,
      int Capacity,
      Guid TopicId,
      string TopicTitle,
      string? TopicCode
  );
}
