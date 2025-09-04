using System;
using System.Collections.Generic;

namespace prjFinalProjectApi.Models;

public partial class RoomOccupancy
{
    public int FOccupancyId { get; set; }

    public int? FMemberId { get; set; } // 可為 null，若無會員

    public int? FBedId { get; set; } // 可為 null，允許未分配床位

    public DateTime? FCheckInDate { get; set; }

    public DateTime? FCheckOutDate { get; set; }

    public bool? FBillingStatus { get; set; } // 保留，標示當前繳費狀態

    public virtual RoomBed FBed { get; set; } = null!;

    public virtual Member? FMember { get; set; } // 添加導航屬性

    // 一對多導航屬性到 RoomPaymentHistory
    public virtual ICollection<RoomPaymentHistory> RoomPaymentHistories { get; set; } = new List<RoomPaymentHistory>();
}