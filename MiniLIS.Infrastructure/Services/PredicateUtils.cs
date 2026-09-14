using System;
using System.Linq.Expressions;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>
    /// Combina expresiones de filtro con Y / O conservándolas traducibles a SQL.
    ///
    /// Encadenar `.Where()` ya da la Y, pero no hay equivalente para la O: hace falta una
    /// única expresión con los términos unidos por OrElse. No se puede combinar con `||` en
    /// C# porque cada lambda trae su propio parámetro; hay que reescribir el segundo para que
    /// use el del primero, que es lo que hace el visitante de abajo.
    /// </summary>
    internal static class PredicateUtils
    {
        public static Expression<Func<T, bool>> O<T>(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b) =>
            Combinar(a, b, Expression.OrElse);

        public static Expression<Func<T, bool>> Y<T>(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b) =>
            Combinar(a, b, Expression.AndAlso);

        private static Expression<Func<T, bool>> Combinar<T>(
            Expression<Func<T, bool>> a,
            Expression<Func<T, bool>> b,
            Func<Expression, Expression, BinaryExpression> operador)
        {
            var parametro = a.Parameters[0];
            var cuerpoB = new ReemplazarParametro(b.Parameters[0], parametro).Visit(b.Body)!;
            return Expression.Lambda<Func<T, bool>>(operador(a.Body, cuerpoB), parametro);
        }

        private sealed class ReemplazarParametro : ExpressionVisitor
        {
            private readonly ParameterExpression _viejo;
            private readonly ParameterExpression _nuevo;

            public ReemplazarParametro(ParameterExpression viejo, ParameterExpression nuevo)
            {
                _viejo = viejo;
                _nuevo = nuevo;
            }

            protected override Expression VisitParameter(ParameterExpression node) =>
                node == _viejo ? _nuevo : base.VisitParameter(node);
        }
    }
}
