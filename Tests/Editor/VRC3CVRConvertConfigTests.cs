#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

// CopyFrom is hand-written, so a new field is silently dropped unless it is added there too.
// This walks the settings instead of listing them, so the next one added is covered.
public class VRC3CVRConvertConfigTests
{
    [Test]
    public void CopyFrom_CopiesEveryPublicBoolAndEnumField()
    {
        var fields = typeof(VRC3CVRConvertConfig)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(field => field.FieldType == typeof(bool) || field.FieldType.IsEnum)
            .ToArray();
        Assert.That(fields.Length, Is.GreaterThan(0), "no fields found - test is not testing anything");

        var source = new VRC3CVRConvertConfig();
        foreach (var field in fields)
        {
            field.SetValue(source, field.FieldType == typeof(bool)
                ? (object)!(bool)field.GetValue(source)
                : Enum.GetValues(field.FieldType).Cast<object>()
                    .First(value => !value.Equals(field.GetValue(source))));
        }

        var destination = new VRC3CVRConvertConfig();
        destination.CopyFrom(source);

        foreach (var field in fields)
        {
            Assert.AreEqual(field.GetValue(source), field.GetValue(destination),
                $"CopyFrom does not copy {field.Name}");
        }
    }
}
#endif
