using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace ScottyApps.Utilities.DbContextExtensions
{
    public class EntityBase
    {
        public EntityBase()
        {
            State = EntityState.Unchanged;
        }
        internal EntityState State { get; private set; }
        internal string[] ModifiedProperties { get; private set; }

        public void MarkAsAdded()
        {
            State = EntityState.Added;
        }
        public void MarkAsModified<T>(Expression<Func<T, object>> exp = null)
            where T : EntityBase
        {
            State = EntityState.Modified;
            if (exp != null)
            {
                var memExp = exp.Body as MemberExpression;
                if (memExp != null)
                {
                    ModifiedProperties = new[] {memExp.Member.Name};
                    return;
                }
                var newExp = exp.Body as NewExpression;
                if (newExp != null)
                {
                    ModifiedProperties = newExp.Members.Select(m => m.Name).ToArray();
                }
            }
        }
        public void MarkAsUnchanged()
        {
            State = EntityState.Unchanged;
        }
        public void MarkAsDeleted()
        {
            State = EntityState.Deleted;
        }
    }
}
