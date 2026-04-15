#!/bin/bash
set -e

# Chạy lệnh SQL bằng quyền của superuser
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-EOSQL
    CREATE DATABASE awe_db;
    CREATE DATABASE keycloak_db;
    GRANT ALL PRIVILEGES ON DATABASE awe_db TO $POSTGRES_USER;
    GRANT ALL PRIVILEGES ON DATABASE keycloak_db TO $POSTGRES_USER;
EOSQL
