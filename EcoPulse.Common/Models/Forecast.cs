using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EcoPulse.Common.Models;

public class Forecast
{
    public int Id { get; set; }
    public string BuildingId { get; set; } = "";
    public DateTime Date { get; set; }
    public double EnergyKWh { get; set; }
    public double WaterM3 { get; set; }
}
