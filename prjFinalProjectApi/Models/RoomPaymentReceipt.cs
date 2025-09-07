using System;
using System.ComponentModel.DataAnnotations;

namespace prjFinalProjectApi.Models;

public partial class RoomPaymentReceipt
{
    public int FReceiptId { get; set; }  // 這應該是主鍵

    public int FPaymentId { get; set; }

    [Required]
    [MaxLength(50)]
    public string FReceiptNumber { get; set; } = null!;

    public DateTime FReceiptDate { get; set; }

    [MaxLength]
    public string? FReceiptFilePath { get; set; }

    [MaxLength]
    public string? FNotes { get; set; }

    public virtual RoomPaymentHistory FPayment { get; set; } = null!;
}