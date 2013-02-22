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
using Wintellect.PowerCollections;

namespace ScottyApps.Utilities.DbContextExtensions
{
    public static class DbContextExtensions
    {
        public static int Delete<T>(this DbSet<T> dbSet, Expression<Func<T, bool>> predicate)
            where T : class
        {
            var query = dbSet.Where(predicate);

            string sql;
            ObjectParameter[] parameters;
            BuildDeleteSql<T>(GetObjectQueryFromDbQuery(query as DbQuery<T>), out sql, out parameters);

            DbContext ctx = GetDbContextFromDbSet(dbSet);
            if (ctx == null)
            {
                throw new Exception("failed on getting DbContext from DbSet");
            }

            var sqlWithParaValue = ReplaceParaWithValue(sql, parameters);
            int rowsAffected = ctx.Database.ExecuteSqlCommand(sqlWithParaValue);
            return rowsAffected;
        }
        private static string ReplaceParaWithValue(string rawSql, ObjectParameter[] paras)
        {
            if (paras == null || paras.Length == 0)
            {
                return rawSql;
            }

            StringBuilder sb = new StringBuilder(rawSql);
            foreach (var p in paras)
            {
                var valStr = p.Value.ToString();
                // TODO need more work for other types, such as Guid, bool
                if (p.ParameterType == typeof (string))
                {
                    valStr = "'" + valStr + "'";
                }
                // TODO the sign "@" is for SQL Server, need more work for other database types
                sb.Replace("@" + p.Name, valStr).Replace(p.Name, valStr);
            }

            return sb.ToString();
        }
        private static void BuildDeleteSql<T>(ObjectQuery query, out string sql, out ObjectParameter[] parameters)
            where T : class
        {
            sql = string.Empty;
            parameters = null;

            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            // NOTE the generated sql contains @ for sql server
            // TODO not sure what would be for other database types
            string origSql = query.ToTraceString().Replace(Environment.NewLine, " ");
            int idxFrom = origSql.IndexOf("from", StringComparison.OrdinalIgnoreCase);
            int idxWhere = origSql.IndexOf("where", StringComparison.OrdinalIgnoreCase);
            string tableWithAlias = origSql.Substring(idxFrom + 4, idxWhere - (idxFrom + 4));
            int idxAs = tableWithAlias.IndexOf("as", StringComparison.OrdinalIgnoreCase);
            string alias = tableWithAlias.Substring(idxAs + 2);

            sql = string.Format("delete {0} from {1} {2}", alias, tableWithAlias, origSql.Substring(idxWhere));
            parameters = query.Parameters.ToArray();
        }
        private static DbContext GetDbContextFromDbSet<T>(DbSet<T> dbSet)
            where T : class
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
        private static ObjectQuery GetObjectQueryFromDbQuery<T>(DbQuery<T> query)
        {
            var binding = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var internalQuery = query.GetType().GetProperty("InternalQuery", binding).GetValue(query, null);
            var objectQuery = internalQuery.GetType().GetProperty("ObjectQuery", binding).GetValue(internalQuery, null);
            return objectQuery as ObjectQuery;
        }

        public static void SaveChanges<TContext>(this TContext ctx, params EntityBase[] toBeUpdatedEntities)
            where TContext : DbContext
        {
            if (toBeUpdatedEntities == null || toBeUpdatedEntities.Length == 0)
            {
                return;
            }

            foreach (var entry in toBeUpdatedEntities)
            {
                AttachObjectWithState(ctx, entry);
            }

            ctx.SaveChanges();
        }
        private static void AttachObjectWithState<TContext>(TContext ctx, EntityBase entry)
            where TContext : DbContext
        {
            if (entry == null)
            {
                return;
            }
            // ReSharper restore HeuristicUnreachableCode
            var entryType = entry.GetType();
            var state = entry.State;
            var dirtyFieldNames = entry.ModifiedProperties;

            var objCtx = GetObjectContext(ctx);
            ObjectStateEntry objStateEntry = null;
            var objInCtx = objCtx.ObjectStateManager.TryGetObjectStateEntry(entry, out objStateEntry);
            var objWalked = objInCtx && objStateEntry.State == state;

            if (objWalked)
            {
                return;
            }
            // attach object itself
            switch (state)
            {
                case EntityState.Unchanged:
                    if (!objInCtx)
                    {
                        ctx.Set(entryType).Attach(entry);
                    }
                    else if(objStateEntry.State != EntityState.Unchanged)
                    {
                        objStateEntry.ChangeState(EntityState.Unchanged);
                    }
                    break;
                case EntityState.Added:
                    if (!objInCtx)
                    {
                        ctx.Set(entryType).Add(entry);
                    }
                    else if (objStateEntry.State != EntityState.Added)
                    {
                        objStateEntry.ChangeState(EntityState.Added);
                    }
                    break;
                case EntityState.Deleted:
                    if (!objInCtx)
                    {
                        ctx.Set(entryType).Remove(entry);
                    }
                    else if (objStateEntry.State != EntityState.Deleted)
                    {
                        objStateEntry.ChangeState(EntityState.Deleted);
                    }
                    break;
                case EntityState.Modified:
                    if (!objInCtx)
                    {
                        ctx.Set(entryType).Attach(entry);
                    }
                    if (!objInCtx || objStateEntry.State != EntityState.Modified)
                    {
                        var dbEntry = ctx.Entry(entry);
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
            var navProps = GetNavProps(ctx, entry);
            if (navProps != null && navProps.Count > 0)
            {
                foreach (var prop in navProps)
                {
                    var val = prop.First.GetValue(entry, null);
                    if (val == null) continue;

                    if (!prop.Second)    // individual child
                    {
                        AttachObjectWithState(ctx, val as EntityBase);
                    }
                    else
                    {
                        var collection = val as IEnumerable;
                        if (collection != null)
                        {
                            foreach (var e in collection.Cast<object>().Where(x => x != null))
                            {
                                AttachObjectWithState(ctx, e as EntityBase);
                            }
                        }
                    }
                }
            }
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
