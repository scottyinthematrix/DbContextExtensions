using System;
using System.Collections.Generic;
using System.Data.Objects;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Wintellect.PowerCollections;

namespace ScottyApps.Utilities.DbContextExtentions
{
    public static class QueryExtensions
    {
        public static List<T> ToPagedList<T>(
            this IOrderedQueryable<T> query,
            int pageIndex,
            int pageSize,
            out int totalRecords)
        {
            totalRecords = 0;
            var entities = new List<T>();

            var pageQuery = from e in query.Skip(pageIndex * pageSize).Take(pageSize)
                            let count = query.Count()
                            select new
                            {
                                Count = count,
                                Entity = e
                            };

            var data = pageQuery.ToList();
            if (data.Count > 0)
            {
                totalRecords = data[0].Count;
                data.ForEach(e => entities.Add(e.Entity));
            }

            return entities;
        }

        public static List<T> ToPagedList<T>(
            this IQueryable<T> query,
            int pageIndex,
            int pageSize,
            out int totalRecords,
            params Pair<Expression<Func<T, dynamic>>, bool>[] sortExps)
        {
            IOrderedQueryable<T> orderedQuery = query.MultipleOrderBy(sortExps);
            return ToPagedList(orderedQuery, pageIndex, pageSize, out totalRecords);
        }

        private static IOrderedQueryable<T> MultipleOrderBy<T>(this IQueryable<T> query, params Pair<Expression<Func<T, dynamic>>, bool /* true, if descending */>[] sortExps)
        {
            IOrderedQueryable<T> orderedQuery = null;
            if (sortExps == null || sortExps.Length < 1)
            {
                throw new InvalidOperationException("at least one sort expression is required.");
            }

            bool firstOne = true;

            foreach (var exp in sortExps)
            {
                if (firstOne)
                {
                    orderedQuery = exp.Second
                                        ? query.OrderByDescending(exp.First)
                                        : query.OrderBy(exp.First);
                    firstOne = false;
                }
                else
                {
                    orderedQuery = exp.Second
                                       ? orderedQuery.ThenByDescending(exp.First)
                                       : orderedQuery.ThenBy(exp.First);
                }
            }

            return orderedQuery;
        }
    }
}
