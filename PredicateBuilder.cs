using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace ScottyApps.Utilities.DbContextExtensions
{
    public class EasyPredicate<T>
    {
        private Expression<Func<T, bool>> _expression;

        public EasyPredicate(Expression<Func<T, bool>> expression)
        {
            _expression = expression;
        }

        public Expression<Func<T, bool>> ToExpressionFunc()
        {
            return _expression;
        }

        #region Predicate Bools
        /// <summary>
        /// Creates a predicate that evaluates to true.
        /// </summary>
        public static EasyPredicate<T> True = EasyPredicate<T>.Create(param => true);

        /// <summary>
        /// Creates a predicate that evaluates to false.
        /// </summary>
        public static EasyPredicate<T> False = EasyPredicate<T>.Create(param => false);
        #endregion

        /// <summary>
        /// Creates a predicate expression from the specified lambda expression.
        /// </summary>
        public static EasyPredicate<T> Create(Expression<Func<T, bool>> expression) { return new EasyPredicate<T>(expression); }

        /// <summary>
        /// Combines the first predicate with the second using the logical "and".
        /// </summary>
        public EasyPredicate<T> And(Expression<Func<T, bool>> expression)
        {
            return Create(Compose(expression, Expression.AndAlso));
        }
        public EasyPredicate<T> And(EasyPredicate<T> predicate)
        {
            return And(predicate._expression);
        }

        /// <summary>
        /// Combines the first predicate with the second using the logical "or".
        /// </summary>
        public EasyPredicate<T> Or(Expression<Func<T, bool>> expression)
        {
            return Create(Compose(expression, Expression.OrElse));
        }
        public EasyPredicate<T> Or(EasyPredicate<T> predicate)
        {
            return Or(predicate._expression);
        }

        /// <summary>
        /// Negates the predicate.
        /// </summary>
        public EasyPredicate<T> Not()
        {
            var negated = Expression.Not(_expression.Body);
            return Create(Expression.Lambda<Func<T, bool>>(negated, _expression.Parameters));
        }

        #region Implicit conversion to and from Expression<Func<TExpressionFuncType, bool>>
        public static implicit operator EasyPredicate<T>(Expression<Func<T, bool>> expression)
        {
            return EasyPredicate<T>.Create(expression);
        }

        public static implicit operator Expression<Func<T, bool>>(EasyPredicate<T> expression)
        {
            return expression._expression;
        }
        #endregion

        #region Operator Overloads
        public static EasyPredicate<T> operator !(EasyPredicate<T> predicate)
        {
            return predicate.Not();
        }

        public static EasyPredicate<T> operator &(EasyPredicate<T> first, EasyPredicate<T> second)
        {
            return first.And(second);
        }

        public static EasyPredicate<T> operator |(EasyPredicate<T> first, EasyPredicate<T> second)
        {
            return first.Or(second);
        }

        //Both should return false so that Short-Circuiting (Conditional Logical Operator ||)
        public static bool operator true(EasyPredicate<T> first) { return false; }
        public static bool operator false(EasyPredicate<T> first) { return false; }
        #endregion

        /// <summary>
        /// Combines the first expression with the second using the specified merge function.
        /// </summary>
        Expression<T> Compose<T>(Expression<T> second, Func<Expression, Expression, Expression> merge)
        {
            // zip parameters (map from parameters of second to parameters of first)
            var map = _expression.Parameters
                .Select((f, i) => new { f, s = second.Parameters[i] })
                .ToDictionary(p => p.s, p => p.f);

            // replace parameters in the second lambda expression with the parameters in the first
            var secondBody = ParameterRebinder.ReplaceParameters(map, second.Body);

            // create a merged lambda expression with parameters from the first expression
            return Expression.Lambda<T>(merge(_expression.Body, secondBody), _expression.Parameters);
        }

        class ParameterRebinder : ExpressionVisitor
        {
            readonly Dictionary<ParameterExpression, ParameterExpression> _map;

            ParameterRebinder(Dictionary<ParameterExpression, ParameterExpression> map)
            {
                this._map = map ?? new Dictionary<ParameterExpression, ParameterExpression>();
            }

            public static Expression ReplaceParameters(Dictionary<ParameterExpression, ParameterExpression> map, Expression exp)
            {
                return new ParameterRebinder(map).Visit(exp);
            }

            protected override Expression VisitParameter(ParameterExpression p)
            {
                ParameterExpression replacement;

                if (_map.TryGetValue(p, out replacement))
                {
                    p = replacement;
                }

                return base.VisitParameter(p);
            }
        }
    }

    /// <summary>
    /// Enables the efficient, dynamic composition of query predicates.
    /// </summary>
    public static class PredicateBuilder
    {
        /// <summary>
        /// Combines the first predicate with the second using the logical "and".
        /// </summary>
        public static EasyPredicate<T> And<T>(this Expression<Func<T, bool>> first, Expression<Func<T, bool>> second)
        {
            return first.ToEasyPredicate<T>().And(second);
        }

        /// <summary>
        /// Combines the first predicate with the second using the logical "or".
        /// </summary>
        public static EasyPredicate<T> Or<T>(this Expression<Func<T, bool>> first, Expression<Func<T, bool>> second)
        {
            return first.ToEasyPredicate<T>().Or(second);
        }

        /// <summary>
        /// Negates the predicate.
        /// </summary>
        public static EasyPredicate<T> Not<T>(this Expression<Func<T, bool>> expression)
        {
            return expression.ToEasyPredicate<T>().Not();
        }

        public static EasyPredicate<T> ToEasyPredicate<T>(this Expression<Func<T, bool>> expression)
        {
            return EasyPredicate<T>.Create(expression);
        }
    }
}
