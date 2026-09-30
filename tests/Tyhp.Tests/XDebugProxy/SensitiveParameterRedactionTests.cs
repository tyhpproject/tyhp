using System.Xml.Linq;
using Tyhp.XDebugProxy.Translation;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class SensitiveParameterRedactionTests
{
    [Theory]
    [InlineData("SensitiveParameterValue")]
    [InlineData("\\SensitiveParameterValue")]
    [InlineData(" SensitiveParameterValue ")]
    [InlineData("sensitiveparametervalue")]
    public void IsSensitiveParameterValueClass_MatchesEngineWrapper(string classname)
    {
        SensitiveParameterRedaction.IsSensitiveParameterValueClass(classname).Should().BeTrue();
        SensitiveParameterRedaction.MustNotUnwrap(classname).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Tyhp\\Decimal")]
    [InlineData("\\Tyhp\\Decimal")]
    [InlineData("Foo\\SensitiveParameterValue")]
    [InlineData("SensitiveParameter")]
    public void IsSensitiveParameterValueClass_RejectsOtherNames(string? classname)
    {
        SensitiveParameterRedaction.IsSensitiveParameterValueClass(classname).Should().BeFalse();
        SensitiveParameterRedaction.MustNotUnwrap(classname).Should().BeFalse();
    }

    [Fact]
    public void TryUnwrap_SensitiveParameterValue_IsRefused()
    {
        XElement property = XElement.Parse(
            """
            <property name="$password" type="object" classname="SensitiveParameterValue" children="1">
              <property name="$value" type="string"><![CDATA[secret]]></property>
            </property>
            """);

        SensitiveParameterRedaction.TryUnwrap(property, out string? innerValue).Should().BeFalse();
        innerValue.Should().BeNull();
        property.Attribute("classname")!.Value.Should().Be("SensitiveParameterValue");
        property.Value.Should().Contain("secret");
    }

    [Fact]
    public void TryUnwrap_LeadingBackslashClassname_IsRefused()
    {
        XElement property = XElement.Parse(
            """
            <property type="object" classname="\SensitiveParameterValue">
              <property name="value" type="string"><![CDATA[secret]]></property>
            </property>
            """);

        SensitiveParameterRedaction.TryUnwrap(property, out string? innerValue).Should().BeFalse();
        innerValue.Should().BeNull();
    }

    [Fact]
    public void TryPreserveWrappedProperty_HidesInnerValue()
    {
        XElement property = XElement.Parse(
            """
            <property name="$password" type="object" classname="SensitiveParameterValue" children="1" numchildren="1">
              <property name="$value" type="string"><![CDATA[secret]]></property>
            </property>
            """);

        SensitiveParameterRedaction.TryPreserveWrappedProperty(property).Should().BeTrue();
        property.Attribute("classname")!.Value.Should().Be("SensitiveParameterValue");
        property.Attribute("type")!.Value.Should().Be("object");
        property.Attribute("children")!.Value.Should().Be("0");
        property.Attribute("numchildren").Should().BeNull();
        property.Elements().Should().BeEmpty();
        property.Value.Should().NotContain("secret");
    }

    [Fact]
    public void TryPreserveWrappedProperty_Decimal_IsNotTreatedAsWrapper()
    {
        XElement property = XElement.Parse(
            """
            <property type="object" classname="Tyhp\Decimal" children="1">
              <property name="$value" type="string"><![CDATA[12.50]]></property>
            </property>
            """);

        SensitiveParameterRedaction.TryPreserveWrappedProperty(property).Should().BeFalse();
        property.Elements().Should().HaveCount(1);
        property.Value.Should().Be("12.50");
    }

    [Fact]
    public void WrapOpaque_DoesNotIncludeRawValue()
    {
        XElement wrapped = SensitiveParameterRedaction.WrapOpaque("$password", "$password");
        wrapped.Attribute("name")!.Value.Should().Be("$password");
        wrapped.Attribute("fullname")!.Value.Should().Be("$password");
        wrapped.Attribute("type")!.Value.Should().Be("object");
        wrapped.Attribute("classname")!.Value.Should().Be("SensitiveParameterValue");
        wrapped.Attribute("children")!.Value.Should().Be("0");
        wrapped.Elements().Should().BeEmpty();
        wrapped.Value.Should().BeEmpty();
        wrapped.ToString().Should().NotContain("secret");
    }

    [Fact]
    public void ReplaceWithOpaqueWrapper_DiscardsRawPayload()
    {
        XElement property = XElement.Parse(
            """
            <property name="$password" type="string"><![CDATA[secret]]></property>
            """);

        SensitiveParameterRedaction.ReplaceWithOpaqueWrapper(property);
        property.Attribute("name")!.Value.Should().Be("$password");
        property.Attribute("type")!.Value.Should().Be("object");
        property.Attribute("classname")!.Value.Should().Be("SensitiveParameterValue");
        property.Value.Should().NotContain("secret");
        property.Elements().Should().BeEmpty();
    }

    [Fact]
    public void ApplyToSynthesizedArgument_MissingMetadata_WrapsOpaqueAndDropsSecret()
    {
        XElement property = XElement.Parse(
            """
            <property name="$password" type="string"><![CDATA[secret]]></property>
            """);

        SensitiveParameterRedaction.ApplyToSynthesizedArgument(property, parameterIsSensitive: null);

        property.Attribute("classname")!.Value.Should().Be("SensitiveParameterValue");
        property.Attribute("type")!.Value.Should().Be("object");
        property.Value.Should().NotContain("secret");
        property.ToString().Should().NotContain("secret");
        property.Elements().Should().BeEmpty();
    }

    [Fact]
    public void ApplyToSynthesizedArgument_MarkedSensitive_WrapsOpaqueAndDropsSecret()
    {
        XElement property = XElement.Parse(
            """
            <property name="$token" type="string"><![CDATA[secret]]></property>
            """);

        SensitiveParameterRedaction.ApplyToSynthesizedArgument(property, parameterIsSensitive: true);

        property.Attribute("classname")!.Value.Should().Be("SensitiveParameterValue");
        property.ToString().Should().NotContain("secret");
    }

    [Fact]
    public void ApplyToSynthesizedArgument_KnownNotSensitive_LeavesRawValue()
    {
        XElement property = XElement.Parse(
            """
            <property name="$user" type="string"><![CDATA[ada]]></property>
            """);

        SensitiveParameterRedaction.ApplyToSynthesizedArgument(property, parameterIsSensitive: false);

        property.Attribute("type")!.Value.Should().Be("string");
        property.Attribute("classname").Should().BeNull();
        property.Value.Should().Be("ada");
    }

    [Fact]
    public void ApplyToSynthesizedArgument_AlreadyWrapped_PreservesAndHidesInnerValue()
    {
        XElement property = XElement.Parse(
            """
            <property name="$password" type="object" classname="SensitiveParameterValue" children="1">
              <property name="$value" type="string"><![CDATA[secret]]></property>
            </property>
            """);

        SensitiveParameterRedaction.ApplyToSynthesizedArgument(property, parameterIsSensitive: false);

        property.Attribute("classname")!.Value.Should().Be("SensitiveParameterValue");
        property.Attribute("type")!.Value.Should().Be("object");
        property.Value.Should().NotContain("secret");
        property.Elements().Should().BeEmpty();
    }

    [Fact]
    public void RedactPropertyTree_NestedWrappers_HideInnerSecrets()
    {
        XElement root = XElement.Parse(
            """
            <property name="$trace" type="array">
              <property name="args" type="array">
                <property name="0" type="object" classname="SensitiveParameterValue" children="1">
                  <property name="$value" type="string"><![CDATA[secret]]></property>
                </property>
              </property>
            </property>
            """);

        SensitiveParameterRedaction.RedactPropertyTree(root);

        XElement wrapper = root.Descendants()
            .Single(e => e.Attribute("classname")?.Value == "SensitiveParameterValue");
        wrapper.Value.Should().NotContain("secret");
        wrapper.Elements().Should().BeEmpty();
        root.ToString().Should().NotContain("secret");
    }
}
