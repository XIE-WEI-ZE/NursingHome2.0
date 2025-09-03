using System;
using System.Collections.Generic;

namespace prjFinalProjectApi.Models;

public partial class RoomOccupancy
{
    public int FOccupancyId { get; set; }

    public int? FMemberId { get; set; } // 可為 null，若無會員

    public int? FBedId { get; set; } // 修改為 int? 以允許 null

    public DateTime? FCheckInDate { get; set; }

    public DateTime? FCheckOutDate { get; set; }

    public int? FBillingAmount { get; set; }

    public DateTime? FBillingDate { get; set; }

    public string? FPaymentMethod { get; set; }

    public bool? FBillingStatus { get; set; }

    public string? FPaypalOrderId { get; set; } // 新增 PayPal 訂單 ID

    public virtual RoomBed FBed { get; set; } = null!;

    public virtual Member? FMember { get; set; } // 添加導航屬性
}