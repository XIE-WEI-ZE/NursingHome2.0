using System;
using System.Collections.Generic;
using prjFinalProjectApi.Models;

public partial class RoomPaymentHistory
{
    public int FPaymentId { get; set; }  // 這應該是主鍵
    public int FOccupancyId { get; set; }
    public int FBillingAmount { get; set; }
    public DateTime FBillingDate { get; set; }
    public string FPaymentMethod { get; set; } = null!;
    public bool FBillingStatus { get; set; }
    public string? FPaypalOrderId { get; set; }
    public virtual RoomOccupancy FOccupancy { get; set; } = null!;
    public virtual ICollection<RoomPaymentReceipt> RoomPaymentReceipts { get; set; } = new List<RoomPaymentReceipt>();
}