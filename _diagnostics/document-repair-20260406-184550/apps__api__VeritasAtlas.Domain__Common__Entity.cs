using System;

namespace VeritasAtlas.Domain.Common
{
    public abstract class Entity
    {
        protected Entity()
        {
            Id = Guid.NewGuid();
            CreatedAtUtc = DateTime.UtcNow;
            UpdatedAtUtc = DateTime.UtcNow;
        }

        public Guid Id { get; protected set; }
        public DateTime CreatedAtUtc { get; protected set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
