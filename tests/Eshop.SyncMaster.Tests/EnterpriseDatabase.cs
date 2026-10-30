using Microsoft.Data.SqlClient;

namespace Eshop.SyncMaster.Tests;

/// <summary>The mock Enterprise System's SQL Server database (fake enterprise schema, see enterprise-schema.sql).</summary>
public sealed class EnterpriseDatabase(string connectionString)
{
    /// <summary>Every row the Enterprise System holds under this Contract number (more than one would be a duplicate).</summary>
    public async Task<IReadOnlyList<EnterpriseContractRow>> ContractsNumberedAsync(string contractNumber)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(
            """
            SELECT contract_no, customer_id_no, customer_name, customer_mobile_no, product_name, cash_price,
                   down_payment, flat_rate, term_months, installment_amount, total_payable, status
            FROM hp.contract WHERE contract_no = @no
            """,
            connection);
        command.Parameters.AddWithValue("@no", contractNumber);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<EnterpriseContractRow>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new EnterpriseContractRow(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetInt32(8), reader.GetDecimal(9),
                reader.GetDecimal(10), reader.GetString(11)));
        }

        return rows;
    }

    /// <summary>Cancels the Contract in the Enterprise System, as its admin tool does.</summary>
    public async Task CancelAsync(string contractNumber)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(
            "UPDATE hp.contract SET status = 'CANCELLED', cancelled_at = SYSDATETIMEOFFSET(), cancel_reason = N'test' WHERE contract_no = @no",
            connection);
        command.Parameters.AddWithValue("@no", contractNumber);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Records a Payment against the Contract, as the Enterprise System's counter or admin tool does.</summary>
    public Task RecordPaymentAsync(string contractNumber, decimal amount, int installmentNumber) =>
        ExecuteAsync(
            "INSERT INTO hp.payment (contract_no, installment_no, amount, paid_at) VALUES (@no, @installment, @amount, SYSDATETIMEOFFSET())",
            ("@no", contractNumber), ("@installment", installmentNumber), ("@amount", amount));

    /// <summary>Sets the Contract's status (ACTIVE | OVERDUE | PAID_OFF | CANCELLED), as the Enterprise System does.</summary>
    public Task SetStatusAsync(string contractNumber, string status) =>
        ExecuteAsync("UPDATE hp.contract SET status = @status WHERE contract_no = @no", ("@no", contractNumber), ("@status", status));

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(ct);
    }
}

public sealed record EnterpriseContractRow(
    string Number, string IdNumber, string CustomerName, string MobileNumber, string ProductName, decimal CashPrice,
    decimal DownPayment, decimal FlatRate, int Term, decimal Installment, decimal TotalPayable, string Status);
