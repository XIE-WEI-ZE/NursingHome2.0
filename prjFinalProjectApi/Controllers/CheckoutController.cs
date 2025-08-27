using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prjFinalProjectApi.Models;
using prjFinalProjectApi.Models.Dto;
using System.Data;

namespace prjFinalProjectApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CheckoutController : ControllerBase
    {
        private readonly DbNursingHomeContext _context;
        public CheckoutController(DbNursingHomeContext context) => _context = context;

        // POST: api/Checkout
        // 建立訂單（會員下單）
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] OrderCreateDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (dto.OrderDetails == null || dto.OrderDetails.Count == 0)
                return BadRequest("訂單明細不可為空");

            // 1) 計算總金額與各項小計
            int total = 0;
            var details = new List<ShopOrderDetail>();

            foreach (var d in dto.OrderDetails)
            {
                var sub = d.UnitPrice * d.Quantity;
                if (sub < 0) sub = 0;

                total += sub;

                details.Add(new ShopOrderDetail
                {
                    ProductId = d.ProductId,
                    ProductName = d.ProductName,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    Subtotal = sub
                });
            }

            // 2) 交易：產生訂單編號、寫入主檔＋明細
            using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var orderNo = await GenerateOrderNoAsync();

            var order = new ShopOrder
            {
                FMemberId = dto.MemberId,
                BuyerName = dto.BuyerName,
                ReceiverName = dto.ReceiverName,
                ReceiverPhone = dto.ReceiverPhone,
                PaymentMethod = dto.PaymentMethod,
                DeliveryMethod = dto.DeliveryMethod,
                DeliveryAddress = dto.DeliveryAddress,
                InvoiceTitle = dto.InvoiceTitle,
                InvoiceTax = dto.InvoiceTax,
                InvoiceInMethod = dto.InvoiceType,   // 發票方式
                CarrierNumber = dto.CarrierNumber,
                OrderTime = DateTime.Now,
                TotalAmount = total,
                Note = dto.Note,
                Status = "未付款",
                OrderNo = orderNo
            };

            _context.ShopOrders.Add(order);
            await _context.SaveChangesAsync();

            foreach (var de in details)
            {
                de.OrderId = order.OrderId;
                _context.ShopOrderDetails.Add(de);
            }
            await _context.SaveChangesAsync();

            await tx.CommitAsync();

            // 前端目前使用 res.merchantTradeNo ?? res.orderNo
            return Ok(new
            {
                success = true,
                orderNo,
                orderId = order.OrderId,
                totalAmount = total
            });
        }

        // 併發安全的當日流水號：ORD-YYYYMMDD-0001
        private async Task<string> GenerateOrderNoAsync()
        {
            string today = DateTime.Now.ToString("yyyyMMdd");
            string prefix = $"ORD-{today}-";

            var last = await _context.ShopOrders
                .Where(o => o.OrderNo != null && o.OrderNo.StartsWith(prefix))
                .OrderByDescending(o => o.OrderNo)
                .Select(o => o.OrderNo)
                .FirstOrDefaultAsync();

            int next = 1;
            if (!string.IsNullOrEmpty(last))
            {
                var tail = last.Split('-').Last(); // "0007"
                if (int.TryParse(tail, out var n)) next = n + 1;
            }
            return prefix + next.ToString("D4");
        }
    }
}
