@echo off
REM Modul 2 - Vault dev sunucusu IN-MEMORY calisir; her "docker compose restart vault"
REM veya container yeniden olusturmada TUM secret'lar kaybolur. Bu script'i o
REM durumlarda TEKRAR calistirin.

set VAULT_CONTAINER=neominal-vault
set VAULT_TOKEN=root

echo Order.Api secreti yukleniyor...
docker exec -e VAULT_TOKEN=%VAULT_TOKEN% %VAULT_CONTAINER% vault kv put secret/order-service "ConnectionStrings:OrderDb=Host=postgres;Port=5432;Database=order_db;Username=neominal;Password=neominal_pass"

echo Payment.Api secreti yukleniyor...
docker exec -e VAULT_TOKEN=%VAULT_TOKEN% %VAULT_CONTAINER% vault kv put secret/payment-service "ConnectionStrings:PaymentDb=Host=postgres;Port=5432;Database=payment_db;Username=neominal;Password=neominal_pass"

echo Inventory.Api secreti yukleniyor...
docker exec -e VAULT_TOKEN=%VAULT_TOKEN% %VAULT_CONTAINER% vault kv put secret/inventory-service "ConnectionStrings:InventoryDb=Host=postgres;Port=5432;Database=inventory_db;Username=neominal;Password=neominal_pass"

echo Tum Vault secretlari yuklendi.
