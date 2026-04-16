using System;
using System.Linq;
using System.Reflection;

namespace HelixToolkit.Nex.ECS.Utils;

internal static class TypeHelper
{
    public static bool IsTagType(this TypeInfo typeInfo) =>
        typeInfo.IsValueType
        && !typeInfo.IsEnum
        && !typeInfo.IsPrimitive
        && typeInfo.DeclaredFields.All(f => f.IsStatic);

    public static bool IsUnmanaged(this TypeInfo typeInfo)
    {
        return typeInfo.IsEnum
            || (
                typeInfo.IsValueType
                && (
                    typeInfo.IsPrimitive
                    || typeInfo
                        .DeclaredFields.Where(f => !f.IsStatic)
                        .All(f => f.FieldType.IsUnmanaged())
                )
            );
    }

    public static bool IsUnmanaged(this Type type) => type.GetTypeInfo().IsUnmanaged();
}
