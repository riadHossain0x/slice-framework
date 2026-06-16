using System.Reflection;
using NetArchTest.Rules;
using Slice.AspNetCore.Mvc;
using Slice.Domain.Entities;
using Slice.Sample.Crm;

namespace Slice.Architecture.Tests;

public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Entity<>).Assembly;
    private static readonly Assembly Crm = typeof(CrmModule).Assembly;

    [Fact]
    public void Domain_has_no_infrastructure_dependencies()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "FluentValidation")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Theory]
    [InlineData("Slice.Sample.Crm.Features.CreateLead", "Slice.Sample.Crm.Features.GetLead")]
    [InlineData("Slice.Sample.Crm.Features.GetLead", "Slice.Sample.Crm.Features.CreateLead")]
    [InlineData("Slice.Sample.Crm.Features.ListLeads", "Slice.Sample.Crm.Features.CreateLead")]
    public void Feature_slices_do_not_reference_each_other(string slice, string otherSlice)
    {
        var result = Types.InAssembly(Crm)
            .That().ResideInNamespace(slice)
            .ShouldNot().HaveDependencyOn(otherSlice)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Slice_controllers_inherit_SliceController()
    {
        var result = Types.InAssembly(Crm)
            .That().HaveNameEndingWith("Controller")
            .Should().Inherit(typeof(SliceController))
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result)
        => result.IsSuccessful
            ? "ok"
            : "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
