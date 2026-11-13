param(
    [long]$BookingId = 0,
    [string]$Api = 'http://127.0.0.1:5045'
)

$ErrorActionPreference = 'Stop'
$config = ConvertFrom-StringData (Get-Content "$PSScriptRoot/../.env" -Raw)
if ($Api -notin @('http://127.0.0.1:5045','http://localhost:5045') -or
    $config.SEPAY_ACCOUNT_NUMBER -ne '0000000000' -or !$config.SEPAY_WEBHOOK_KEY) {
    throw 'Use the local demo API with the fake SePay account and webhook key.'
}

function Invoke-BookingApi($method, $path, $body = $null, $headers = @{}) {
    $options = @{ Uri = "$Api/api$path"; Method = $method; Headers = $headers }
    if ($null -ne $body) {
        $options.ContentType = 'application/json; charset=utf-8'
        $options.Body = ConvertTo-Json -InputObject $body -Compress
    }
    $response = Invoke-RestMethod @options
    $response
}

$login = Invoke-BookingApi POST '/auth/login' @{
    email = $config.ADMIN_EMAIL
    password = $config.ADMIN_PASSWORD
}
$admin = @{ Authorization = "Bearer $($login.token)" }

if ($BookingId -gt 0) {
    $detail = Invoke-BookingApi GET "/bookings/$BookingId" $null $admin
    if ($detail.booking.status -ne 'PendingPayment' -or !$detail.payment) {
        throw 'Choose a booking that is still waiting for payment.'
    }
    $transactionId = [long]([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()) * 1000 + (Get-Random -Minimum 0 -Maximum 1000)
    $reply = Invoke-BookingApi POST '/payments/sepay/webhook' @{
        id = $transactionId
        accountNumber = '0000000000'
        content = $detail.payment.code
        transferType = 'in'
        transferAmount = $detail.payment.amountVnd
    } @{ Authorization = "Apikey $($config.SEPAY_WEBHOOK_KEY)" }
    $result = Invoke-BookingApi GET "/bookings/$BookingId" $null $admin
    if (!$reply.success -or $result.booking.status -ne 'Confirmed' -or $result.payment.status -ne 'Paid') {
        throw 'The booking did not confirm. Check expiry and API logs.'
    }
    [pscustomobject]@{ BookingId = $BookingId; BookingStatus = $result.booking.status; PaymentStatus = $result.payment.status }
    return
}

$branches = @(Invoke-BookingApi GET '/admin/branches' $null $admin)
foreach ($name in @('Chi nhánh Quận 7', 'Chi nhánh Thủ Đức')) {
    $branch = $branches | Where-Object { $_.Name -eq $name -and $_.IsActive } | Select-Object -First 1
    if (!$branch) {
        $branch = Invoke-BookingApi POST '/admin/branches' @{
            name = $name
            address = 'Địa điểm demo'
            area = $name.Substring('Chi nhánh '.Length)
            openingHour = 6
            closingHour = 23
        } $admin
    }
    $path = "/management/branches/$($branch.id)/prices"
    $prices = @(Invoke-BookingApi GET $path $null $admin)
    if ($prices.Count -eq 0) {
        $null = Invoke-BookingApi POST $path @{
            effectiveFrom = '2025-01-01'
            regularPriceVnd = 250000
            peakPriceVnd = 350000
            peakStartHour = 18
            peakEndHour = 22
        } $admin
    }
    $pitchPath = "/management/branches/$($branch.id)/pitches"
    $pitches = @(Invoke-BookingApi GET $pitchPath $null $admin)
    if ($pitches.Count -eq 0) {
        $pitches = @(foreach ($number in 1..3) {
            Invoke-BookingApi POST $pitchPath @{ name = "Sân $number"; isActive = $true } $admin
        })
    }
    [pscustomobject]@{ BranchId = $branch.id; Branch = $branch.name; Pitches = $pitches.Count }
}
