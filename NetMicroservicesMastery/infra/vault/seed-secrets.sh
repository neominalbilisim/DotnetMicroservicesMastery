#!/bin/bash
# Modül 2 - Vault dev sunucusu IN-MEMORY çalışır; her `docker compose restart vault`
# veya container yeniden oluşturmada TÜM secret'lar kaybolur. Bu script'i o
# durumlarda TEKRAR çalıştırın.
set -e

VAULT_CONTAINER="neominal-vault"
VAULT_TOKEN="root"

echo "Order.Api secret'ı yükleniyor..."
docker exec -e VAULT_TOKEN="$VAULT_TOKEN" "$VAULT_CONTAINER" vault kv put secret/order-service \
  "ConnectionStrings:OrderDb=Host=postgres;Port=5432;Database=order_db;Username=neominal;Password=neominal_pass"

echo "Payment.Api secret'ı yükleniyor..."
docker exec -e VAULT_TOKEN="$VAULT_TOKEN" "$VAULT_CONTAINER" vault kv put secret/payment-service \
  "ConnectionStrings:PaymentDb=Host=postgres;Port=5432;Database=payment_db;Username=neominal;Password=neominal_pass"

echo "Inventory.Api secret'ı yükleniyor..."
docker exec -e VAULT_TOKEN="$VAULT_TOKEN" "$VAULT_CONTAINER" vault kv put secret/inventory-service \
  "ConnectionStrings:InventoryDb=Host=postgres;Port=5432;Database=inventory_db;Username=neominal;Password=neominal_pass"

echo "Tüm Vault secret'ları yüklendi."
