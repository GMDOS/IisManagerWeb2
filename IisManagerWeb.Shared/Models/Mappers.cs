using System.Linq;
using Microsoft.Web.Administration;

namespace IisManagerWeb.Shared.Models;

public static class SiteMapper
{
    public static SiteDto FromSite(Site site)
    {
        var dto = new SiteDto
        {
            Name = site.Name,
            Id = site.Id,
            ServerAutoStart = site.ServerAutoStart.ToString(),
            State = site.State,
            Applications = site.Applications.Select(app => ApplicationMapper.FromApplication(app)).ToList(),
            Bindings = site.Bindings.Select(b => BindingMapper.FromBinding(b)).ToList()
        };
        return dto;
    }
}

public static class ApplicationMapper
{
    public static ApplicationDto FromApplication(Application app)
    {
        var dto = new ApplicationDto
        {
            Path = app.Path,
            ApplicationPoolName = app.ApplicationPoolName,
            VirtualDirectories = app.VirtualDirectories
                .Select(vd => VirtualDirectoryMapper.FromVirtualDirectory(vd)).ToList()
        };
        return dto;
    }
}

public static class VirtualDirectoryMapper
{
    public static VirtualDirectoryDto FromVirtualDirectory(VirtualDirectory vd)
    {
        var dto = new VirtualDirectoryDto
        {
            Path = vd.Path,
            PhysicalPath = vd.PhysicalPath
        };
        return dto;
    }
}

public static class BindingMapper
{
    public static BindingDto FromBinding(Binding binding)
    {
        var dto = new BindingDto
        {
            Protocol = binding.Protocol,
            BindingInformation = binding.BindingInformation,
            Host = binding.Host,
            EndPoint = binding.EndPoint?.ToString() ?? string.Empty
        };
        return dto;
    }
} 