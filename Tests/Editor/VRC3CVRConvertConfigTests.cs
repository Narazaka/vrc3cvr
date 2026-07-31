#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using System.Reflection;
using NUnit.Framework;

// CopyFrom is hand-written, so a new field is silently dropped unless it is added there too.
// This walks every public bool field instead of listing them, so the next added flag is covered.
public class VRC3CVRConvertConfigTests
{
    [Test]
    public void CopyFrom_CopiesEveryPublicBoolField()
    {
        var boolFields = typeof(VRC3CVRConvertConfig)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(field => field.FieldType == typeof(bool))
            .ToArray();
        Assert.That(boolFields.Length, Is.GreaterThan(0), "no bool fields found - test is not testing anything");

        var source = new VRC3CVRConvertConfig();
        foreach (var field in boolFields)
        {
            field.SetValue(source, !(bool)field.GetValue(source));
        }

        var destination = new VRC3CVRConvertConfig();
        destination.CopyFrom(source);

        foreach (var field in boolFields)
        {
            Assert.AreEqual(field.GetValue(source), field.GetValue(destination),
                $"CopyFrom does not copy {field.Name}");
        }
    }
}
#endif
