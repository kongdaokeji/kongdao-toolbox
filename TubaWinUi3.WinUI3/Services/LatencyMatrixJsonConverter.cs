using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

internal sealed class LatencyMatrixJsonConverter : JsonConverter<InterCoreLatencyMatrix>
{
	public override InterCoreLatencyMatrix Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var doc = JsonDocument.ParseValue(ref reader);
		var root = doc.RootElement;
		int coreCount = root.TryGetProperty("CoreCount", out var p1) ? p1.GetInt32() : 0;
		double averageNs = root.TryGetProperty("AverageNs", out var p2) ? p2.GetDouble() : 0.0;
		double minNs = root.TryGetProperty("MinNs", out var p3) ? p3.GetDouble() : 0.0;
		double maxNs = root.TryGetProperty("MaxNs", out var p4) ? p4.GetDouble() : 0.0;
		double[,]? array = null;
		if (root.TryGetProperty("Latencies", out var latElement) && latElement.ValueKind == JsonValueKind.Array)
		{
			int rows = latElement.GetArrayLength();
			if (rows > 0)
			{
				int cols = latElement[0].GetArrayLength();
				array = new double[rows, cols];
				for (int i = 0; i < rows; i++)
				{
					var row = latElement[i];
					for (int j = 0; j < cols; j++)
					{
						array[i, j] = row[j].GetDouble();
					}
				}
			}
		}
		return new InterCoreLatencyMatrix
		{
			CoreCount = coreCount,
			Latencies = array ?? new double[0, 0],
			AverageNs = averageNs,
			MinNs = minNs,
			MaxNs = maxNs
		};
	}

	public override void Write(Utf8JsonWriter writer, InterCoreLatencyMatrix value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WriteNumber("CoreCount", value.CoreCount);
		writer.WriteNumber("AverageNs", value.AverageNs);
		writer.WriteNumber("MinNs", value.MinNs);
		writer.WriteNumber("MaxNs", value.MaxNs);
		writer.WriteStartArray("Latencies");
		int rows = value.Latencies.GetLength(0);
		int cols = value.Latencies.GetLength(1);
		for (int i = 0; i < rows; i++)
		{
			writer.WriteStartArray();
			for (int j = 0; j < cols; j++)
			{
				writer.WriteNumberValue(value.Latencies[i, j]);
			}
			writer.WriteEndArray();
		}
		writer.WriteEndArray();
		writer.WriteEndObject();
	}
}
