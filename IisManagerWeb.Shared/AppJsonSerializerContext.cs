using System.Text.Json.Serialization;
using IisManagerWeb.Shared.Models;

namespace IisManagerWeb.Shared;

[JsonSerializable(typeof(ServerMetrics))]
[JsonSerializable(typeof(SiteMetrics))]
[JsonSerializable(typeof(ServerMetricsHistory))]
[JsonSerializable(typeof(MetricsWebSocketPacket))]
[JsonSerializable(typeof(object))] // Para objetos anônimos
public partial class AppJsonSerializerContext : JsonSerializerContext
{
} 