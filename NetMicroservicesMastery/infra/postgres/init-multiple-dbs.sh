#!/bin/bash
# Modül 1/4 - "Database per Service": her mikroservis kendi veritabanına sahiptir.
# Tek bir Postgres container'ında (docker-compose.infra.yml -> postgres),
# varsayılan POSTGRES_DB (neominal_demo) yanına EK veritabanları oluşturur.
# NOT: keycloak_db burada YOK — sizin Keycloak (21.1.1) kurulumunuz gömülü/dev
# mode veritabanı kullanıyor, harici Postgres'e bağlı değil.
set -e

for db in order_db payment_db inventory_db hangfire_db saga_db; do
  echo "Creating database: $db"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-EOSQL
    CREATE DATABASE $db;
EOSQL
done
