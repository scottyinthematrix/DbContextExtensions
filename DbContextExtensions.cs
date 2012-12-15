using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Objects;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Wintellect.PowerCollections;

namespace ScottyApps.Utilities.DbContextExtentions
{
    public static class DbContextExtensions
    {
        public static int Delete<TEntry>(this DbSet<TEntry> dbSet, Expression<Func<TEntry, bool>> predicate)
            where TEntry : class
        {
            var query = dbSet.Where(predicate);

            string sql;
            object[] parameters;
            BuildDeleteSql<TEntry>(GetObjectQueryFromDbQuery(query as DbQuery<TEntry>), out sql, out parameters);

            DbContext ctx = GetDbContextFromDbSet(dbSet);
            if (ctx == null)
            {
                throw new Exception("failed on getting DbContext from DbSet");
            }

            int rowsAffected = ctx.Database.ExecuteSqlCommand(sql, parameters);
            return rowsAffected;
        }
        private static void BuildDeleteSql<TEntry>(ObjectQuery query, out string sql, out object[] parameters)
            where TEntry : class
        {
            sql = string.Empty;
            parameters = null;

            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            string origSql = query.ToTraceString().Replace(Environment.NewLine, " ");
            int idxFrom = origSql.IndexOf("from", StringComparison.OrdinalIgnoreCase);
            int idxWhere = origSql.IndexOf("where", StringComparison.OrdinalIgnoreCase);
            string tableWithAlias = origSql.Substring(idxFrom + 4, idxWhere - (idxFrom + 4));
            int idxAs = tableWithAlias.IndexOf("as", StringComparison.OrdinalIgnoreCase);
            string alias = tableWithAlias.Substring(idxAs + 2);

            sql = string.Format("delete {0} from {1} {2}", alias, tableWithAlias, origSql.Substring(idxWhere));
            parameters = query.Parameters.ToArray();
        }
        private static DbContext GetDbContextFromDbSet<TEntry>(DbSet<TEntry> dbSet)
            where TEntry : class
        {
            var binding = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            object internalSet = dbSet.GetType().GetField("_internalSet", binding).GetValue(dbSet);
            object internalContext = internalSet.GetType().GetProperty("InternalContext", binding).GetValue(internalSet, null);
            object context = internalContext.GetType().GetProperty("Owner", binding).GetValue(internalContext, null);

            return context as DbContext;
        }
        private static ObjectContext GetObjectContext(DbContext ctx)
        {
            return (ctx as IObjectContextAdapter).ObjectContext;
        }
        private static ObjectQuery GetObjectQueryFromDbQuery<TEntry>(DbQuery<TEntry> query)
        {
            var binding = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var internalQuery = query.GetType().GetProperty("InternalQuery", binding).GetValue(query, null);
            var objectQuery = internalQuery.GetType().GetProperty("ObjectQuery", binding).GetValue(internalQuery, null);
            return objectQuery as ObjectQuery;
        }

        public static void SaveChanges<TContext>(this TContext ctx, List<Triple<object, EntityState, string[]>> toBeUpdatedEntities)
            where TContext : DbContext
        {
            if (toBeUpdatedEntities == null || toBeUpdatedEntities.Count == 0)
            {
                return;
            }

            foreach (var entry in toBeUpdatedEntities)
            {
                AttachObjectWithState(ctx, toBeUpdatedEntities, entry);
            }

            ctx.SaveChanges();
        }
        private static void AttachObjectWithState<TContext>(TContext ctx, List<Triple<object, EntityState, string[]>> triples, Triple<object, EntityState, string[]> entry)
            where TContext : DbContext
        {
            // ReSharper disable ConditionIsAlwaysTrueOrFalse
            if (entry == null)
            // ReSharper restore ConditionIsAlwaysTrueOrFalse
            // ReSharper disable HeuristicUnreachableCode
            {
                return;
            }
            // ReSharper restore HeuristicUnreachableCode
            var entityEntry = entry.First;
            var entryType = entityEntry.GetType();
            var state = entry.Second;
            var dirtyFieldNames = entry.Third;

            var objCtx = GetObjectContext(ctx);
            ObjectStateEntry objStateEntry = null;
            var objInCtx = objCtx.ObjectStateManager.TryGetObjectStateEntry(entityEntry, out objStateEntry);
            var objWalked = objInCtx && objStateEntry.State == state;

            if (objWalked)
            {
                return;
            }
            // attach object itself
            switch (state)
            {
                case EntityState.Added:
                    if (!objInCtx)
                    {
                        ctx.Set(entryType).Add(entityEntry);
                    }
                    else if (objStateEntry.State != EntityState.Added)
                    {
                        objStateEntry.ChangeState(EntityState.Added);
                    }
                    break;
                case EntityState.Deleted:
                    if (!objInCtx)
                    {
                        ctx.Set(entryType).Remove(entityEntry);
                    }
                    else if (objStateEntry.State != EntityState.Deleted)
                    {
                        objStateEntry.ChangeState(EntityState.Deleted);
                    }
                    break;
                case EntityState.Modified:
                    if (!objInCtx)
                    {
                        ctx.Set(entryType).Attach(entityEntry);
                    }
                    if (!objInCtx || objStateEntry.State != EntityState.Modified)
                    {
                        var dbEntry = ctx.Entry(entityEntry);
                        if (dirtyFieldNames != null && dirtyFieldNames.Length > 0)
                        {
                            dirtyFieldNames.ToList().ForEach(f => dbEntry.Property(f).IsModified = true);
                        }
                        else
                        {
                            dbEntry.State = EntityState.Modified;
                        }
                    }
                    break;
            }
            // attach navigation objects belong to this object
            var navProps = GetNavProps(ctx, entityEntry);
            if (navProps != null && navProps.Count > 0)
            {
                foreach (var prop in navProps)
                {
                    var val = prop.First.GetValue(entityEntry, null);
                    if (val == null) continue;

                    if (!prop.Second)    // individual child
                    {
                        AttachObjectWithState(ctx, triples, FindTriple(triples, val));
                    }
                    else
                    {
                        var collection = val as IEnumerable;
                        if (collection != null)
                        {
                            foreach (var entity in collection.Cast<object>().Where(entity => entity != null))
                            {
                                AttachObjectWithState(ctx, triples, FindTriple(triples, entity));
                            }
                        }
                    }
                }
            }
        }

        private static Triple<object, EntityState, string[]> FindTriple(List<Triple<object, EntityState, string[]>> entryList, object entity)
        {
            return entryList.Find(e => ReferenceEquals(e.First, entity));
        }

        private static List<Type> _allowedEntityTypes = null;
        public static List<Type> GetAllowedEntityTypes<TContext>(TContext ctx)
            where TContext : DbContext
        {
            if (_allowedEntityTypes != null)
            {
                return _allowedEntityTypes;
            }

            var query = from p in ctx.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        let propType = p.PropertyType
                        where propType.IsGenericType && propType.Name == /*(typeof(DbSet<>))*/"DbSet`1"
                        select propType.GetGenericArguments()[0];
            _allowedEntityTypes = query.ToList();
            return _allowedEntityTypes;
        }

        private static bool IsEntityTypeInContext<TContext>(TContext ctx, Type entityType)
            where TContext : DbContext
        {
            var allEntityTypes = GetAllowedEntityTypes(ctx);
            return allEntityTypes.Exists(t => t.IsAssignableFrom(entityType));
        }

        private static List<Pair<PropertyInfo, bool /* true if it is ICollection<> */>> GetNavProps<TContext>(TContext ctx, object entry)
            where TContext : DbContext
        {
            var propList = new List<Pair<PropertyInfo, bool>>();

            var allEntityTypes = GetAllowedEntityTypes(ctx);

            foreach (var p in entry.GetType().GetProperties())
            {
                if (p.PropertyType.IsGenericType
                    && p.PropertyType.Name == "ICollection`1")
                {
                    Type[] argTypes = p.PropertyType.GetGenericArguments();
                    if (argTypes.Length == 1
                        && IsEntityTypeInContext(ctx, argTypes[0]))
                    {
                        propList.Add(new Pair<PropertyInfo, bool>(p, true));
                    }
                }
                else if (IsEntityTypeInContext(ctx, p.PropertyType))
                {
                    propList.Add(new Pair<PropertyInfo, bool>(p, false));
                }
            }

            return propList;
        }
    }
}
