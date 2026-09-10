using System.Reflection;
using Web.Api;

namespace ArchitectureTests;

public abstract class BaseTest
{
    protected static readonly Assembly WebApiAssembly = typeof(DependencyInjection).Assembly;
}
