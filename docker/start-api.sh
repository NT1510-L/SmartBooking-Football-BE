#!/bin/bash
set -eu

if [[ ${Pricing__RunLocal:-false} != true || ${RunMode:-} == Worker ]]; then
    exec dotnet /app/Football_Booking.dll
fi

if [[ -z ${ConnectionStrings__Pricing:-} ]]; then
    echo 'Local Pricing requires ConnectionStrings__Pricing. Starting the API with its configured Pricing address.' >&2
    exec dotnet /app/Football_Booking.dll
fi

PricingPort=5081 PricingLoopbackOnly=true dotnet /pricing/Pricing.dll &
pricing_pid=$!
Pricing__Address=http://127.0.0.1:5081 dotnet /app/Football_Booking.dll &
api_pid=$!

stop_services() {
    kill "$api_pid" "$pricing_pid" 2>/dev/null || true
    wait "$api_pid" "$pricing_pid" 2>/dev/null || true
}

trap 'stop_services; exit 0' TERM INT
status=0
wait -n "$api_pid" "$pricing_pid" || status=$?
stop_services
exit "$status"
