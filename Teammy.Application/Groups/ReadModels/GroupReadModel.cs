using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Teammy.Application.Groups.ReadModels
{
    public sealed class GroupReadModel
    {
        public Guid Id { get; set; }
        public Guid TermId { get; set; }
        public Guid? TopicId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Capacity { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? TopicTitle { get; set; }
        public string? TopicCode { get; set; }
        public int Members { get; set; }
    }
}
