#!/bin/bash
(
    set -eu
    for password in "$BOOKING_DB_PASSWORD" "$PRICING_DB_PASSWORD"; do
        if [[ ${#password} -lt 24 ]]; then
            echo "Use database passwords with at least 24 characters." >&2
            exit 1
        fi
    done
    MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql -uroot <<SQL
CREATE USER 'booking_app'@'%' IDENTIFIED BY '$BOOKING_DB_PASSWORD';
GRANT SELECT, INSERT, UPDATE, DELETE ON booking_db.* TO 'booking_app'@'%';
CREATE USER 'pricing_app'@'%' IDENTIFIED BY '$PRICING_DB_PASSWORD';
GRANT SELECT, INSERT, UPDATE, DELETE ON pricing_db.* TO 'pricing_app'@'%';
SQL
)
