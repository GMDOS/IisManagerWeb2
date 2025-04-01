using System.Collections.Generic;
using Microsoft.Web.Administration;

namespace IisManagerWeb.Shared.Models;

public class SiteDto
{
    public string Name { get; set; } = string.Empty;
    public long Id { get; set; }
    public string ServerAutoStart { get; set; } = string.Empty;
    public ObjectState State { get; set; }
    public List<ApplicationDto> Applications { get; set; } = new();
    public List<BindingDto> Bindings { get; set; } = new();
}

public class ApplicationDto
{
    public string Path { get; set; } = string.Empty;
    public string ApplicationPoolName { get; set; } = string.Empty;
    public List<VirtualDirectoryDto> VirtualDirectories { get; set; } = new();
}

public class VirtualDirectoryDto
{
    public string Path { get; set; } = string.Empty;
    public string PhysicalPath { get; set; } = string.Empty;
}

public class BindingDto
{
    public string Protocol { get; set; } = string.Empty;
    public string BindingInformation { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string EndPoint { get; set; } = string.Empty;
}

public class SiteGroupDto
{
    public string Name { get; set; } = string.Empty;
    public List<string> SiteNames { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
} 