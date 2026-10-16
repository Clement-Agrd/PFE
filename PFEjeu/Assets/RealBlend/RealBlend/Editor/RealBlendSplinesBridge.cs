using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace RealBlend
{
    /// <summary>
    /// Editor-only access to the optional Unity Splines package. Compile the
    /// adapters once so tunnel previews use the original APIs without reflection
    /// or boxed Mathematics vectors for every point along the spline.
    /// </summary>
    internal static class RealBlendSplinesBridge
    {
        private delegate bool EvaluateSpline(Component container, int index, float t,
            out Vector3 position, out Vector3 tangent, out Vector3 up);

        internal static readonly Type ContainerType;
        internal static readonly bool IsAvailable;
        internal static readonly string UnavailableMessage =
            "Spline tunnels require the optional Unity Splines package. Install Splines " +
            "from Package Manager to use this mode. Painting, sculpting, and the other " +
            "creation modes work without it.";

        private static readonly Func<Component, int> Count;
        private static readonly Func<Component, int, float> Length;
        private static readonly EvaluateSpline Evaluate;

        static RealBlendSplinesBridge()
        {
            try
            {
                ContainerType = Type.GetType("UnityEngine.Splines.SplineContainer, Unity.Splines", false);
                if (ContainerType == null)
                    return;

                ParameterExpression container = Expression.Parameter(typeof(Component), "container");
                ParameterExpression index = Expression.Parameter(typeof(int), "index");
                UnaryExpression instance = Expression.Convert(container, ContainerType);

                PropertyInfo splines = ContainerType.GetProperty("Splines");
                Type collection = typeof(IReadOnlyCollection<>).MakeGenericType(splines.PropertyType.GetGenericArguments()[0]);
                Count = Expression.Lambda<Func<Component, int>>(
                    Expression.Property(Expression.Convert(Expression.Property(instance, splines), collection), "Count"),
                    container).Compile();

                MethodInfo calculateLength = ContainerType.GetMethod("CalculateLength", new[] { typeof(int) });
                Length = Expression.Lambda<Func<Component, int, float>>(
                    Expression.Call(instance, calculateLength, index), container, index).Compile();

                MethodInfo evaluate = FindEvaluateMethod();
                if (evaluate == null)
                    throw new MissingMethodException("SplineContainer.Evaluate(int, float, out position, out tangent, out up)");

                Type vectorType = evaluate.GetParameters()[2].ParameterType.GetElementType();
                ParameterExpression t = Expression.Parameter(typeof(float), "t");
                ParameterExpression position = Expression.Parameter(typeof(Vector3).MakeByRefType(), "position");
                ParameterExpression tangent = Expression.Parameter(typeof(Vector3).MakeByRefType(), "tangent");
                ParameterExpression up = Expression.Parameter(typeof(Vector3).MakeByRefType(), "up");
                ParameterExpression positionValue = Expression.Variable(vectorType, "positionValue");
                ParameterExpression tangentValue = Expression.Variable(vectorType, "tangentValue");
                ParameterExpression upValue = Expression.Variable(vectorType, "upValue");
                ParameterExpression success = Expression.Variable(typeof(bool), "success");

                Evaluate = Expression.Lambda<EvaluateSpline>(Expression.Block(
                    new[] { positionValue, tangentValue, upValue, success },
                    Expression.Assign(success, Expression.Call(instance, evaluate, index, t, positionValue, tangentValue, upValue)),
                    Expression.Assign(position, ToVector3(positionValue)),
                    Expression.Assign(tangent, ToVector3(tangentValue)),
                    Expression.Assign(up, ToVector3(upValue)),
                    success), container, index, t, position, tangent, up).Compile();
                IsAvailable = true;
            }
            catch (Exception exception)
            {
                // A missing or incompatible package must never prevent the other
                // tools from opening. The Spline tab explains how to resolve it.
                UnavailableMessage = "RealBlend could not use the installed Unity Splines package. " +
                    "Check or update Splines in Package Manager. The other RealBlend tools remain available.\n" +
                    exception.Message;
            }
        }

        internal static bool IsContainer(Component container)
        {
            return IsAvailable && container != null && ContainerType.IsInstanceOfType(container);
        }

        internal static int GetSplineCount(Component container)
        {
            return IsContainer(container) ? Count(container) : 0;
        }

        internal static float CalculateLength(Component container, int index)
        {
            return IsContainer(container) ? Length(container, index) : 0f;
        }

        internal static bool TryEvaluate(Component container, int index, float t,
            out Vector3 position, out Vector3 tangent, out Vector3 up)
        {
            position = Vector3.zero;
            tangent = Vector3.forward;
            up = Vector3.up;
            return IsContainer(container) && Evaluate(container, index, t, out position, out tangent, out up);
        }

        private static MethodInfo FindEvaluateMethod()
        {
            foreach (MethodInfo method in ContainerType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.Name != "Evaluate" || method.IsGenericMethod || method.ReturnType != typeof(bool))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 5 && parameters[0].ParameterType == typeof(int) &&
                    parameters[1].ParameterType == typeof(float) && parameters[2].IsOut &&
                    parameters[3].ParameterType == parameters[2].ParameterType &&
                    parameters[4].ParameterType == parameters[2].ParameterType)
                    return method;
            }
            return null;
        }

        private static NewExpression ToVector3(Expression value)
        {
            return Expression.New(typeof(Vector3).GetConstructor(new[] { typeof(float), typeof(float), typeof(float) }),
                Expression.Field(value, "x"), Expression.Field(value, "y"), Expression.Field(value, "z"));
        }
    }
}
