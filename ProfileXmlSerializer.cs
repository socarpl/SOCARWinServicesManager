using System.IO;
using System.Xml.Linq;

namespace Socar.WinServicesManager;

public static class ProfileXmlSerializer
{
    public static void Save(ServiceProfile profile, string path)
    {
        var document = new XDocument(
            new XElement("serviceProfile",
                new XAttribute("name", profile.Name),
                new XElement("actions",
                    profile.Actions.Select(action =>
                        new XElement("service",
                            new XAttribute("name", action.ServiceName),
                            new XAttribute("displayName", action.DisplayName ?? string.Empty),
                            action.DesiredStartType is null
                                ? null
                                : new XAttribute("startupType", action.DesiredStartType.Value.ToString()),
                            action.DesiredStatus is null
                                ? null
                                : new XAttribute("status", action.DesiredStatus.Value.ToString()))))));

        document.Save(path);
    }

    public static ServiceProfile Load(string path)
    {
        var document = XDocument.Load(path);
        var root = document.Root;
        if (root?.Name != "serviceProfile")
        {
            throw new InvalidOperationException("The selected XML file is not a service profile export.");
        }

        var profileName = ((string?)root.Attribute("name"))?.Trim();
        if (string.IsNullOrWhiteSpace(profileName))
        {
            profileName = Path.GetFileNameWithoutExtension(path);
        }

        var profile = new ServiceProfile
        {
            Name = profileName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var element in root.Element("actions")?.Elements("service") ?? [])
        {
            var serviceName = ((string?)element.Attribute("name"))?.Trim();
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                continue;
            }

            var startupText = ((string?)element.Attribute("startupType"))?.Trim();
            var statusText = ((string?)element.Attribute("status"))?.Trim();

            profile.Actions.Add(new ProfileServiceAction
            {
                ServiceName = serviceName,
                DisplayName = ((string?)element.Attribute("displayName"))?.Trim(),
                DesiredStartType = Enum.TryParse<ServiceStartType>(startupText, out var startType) ? startType : null,
                DesiredStatus = Enum.TryParse<DesiredServiceStatus>(statusText, out var status) ? status : null
            });
        }

        if (profile.Actions.Count == 0)
        {
            throw new InvalidOperationException("The selected XML profile does not contain any service actions.");
        }

        return profile;
    }
}
