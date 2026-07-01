# API — Endpoints

Todos exigem `Authorization: Bearer <firebase-jwt>`, exceto onde indicado. `owner_id` resolvido do token. Derivados (`effective`, `myShare`, `receivable`) calculados na resposta, não persistidos.

## Infra
- `GET /health` → health check (sem auth).

## Identidade
- `GET /me` → perfil do app_user (id, name, email). Provisiona se novo.

## Cadastros (soft delete — **sem prefixo `/api`**)
- `/categories` — GET, POST, PUT `/{id}`, DELETE `/{id}` (desativa)
- `/persons` — idem
- `/incomes` — idem (molde: kind, default_amount)
- `/bills` — idem (molde: category_id, kind, default_amount, split_ratio, person_id). Regra: split<1 exige person_id; =1 proíbe.

## Projeção
- `POST /api/projection/{year}` → gera 12 entries por molde recorrente ativo. Idempotente. Resp: {billEntriesCreated, incomeEntriesCreated, skipped}.

## Lançamentos
- `GET /api/entries?year=&month=` → bills[], incomes[], totals (com derivados). `totals.receivable` = a receber **pendente** (bill entries com `received=false`); `totals.received` = já recebido no mês. `received + receivable` = total a receber do mês.
- `POST /api/entries/bill` {billId,year,month,plannedAmount} → 201 / 409 (duplicado).
- `POST /api/entries/income` {incomeId,year,month,plannedAmount}.
- `DELETE /api/entries/bill/{id}` → 204 se não pago; 409 se pago. `DELETE /api/entries/income/{id}` idem (received).
- `PATCH /api/entries/bill/{id}` {plannedAmount?,actualAmount?} → 200 / 409 se congelado. `PATCH /api/entries/income/{id}` idem.
- `POST /api/entries/bill/{id}/pay` {actualAmount?,paidDate?} → congela. `/unpay` → descongela.
- `POST /api/entries/income/{id}/receive` / `/unreceive`.

## Recálculo
- `POST /api/bills/{billId}/recalculate` {fromYear,fromMonth,newAmount} → atualiza default_amount + planned dos não-pagos ≥ mês. Resp: {updatedEntries, skippedPaid, newDefaultAmount}.

## Dashboards
- `GET /api/dashboard/month?year=&month=` → summary + byCategory (myShare previsto/real/diff).
- `GET /api/dashboard/year?year=` → months[12] + byCategory + totals.

## A receber
- `GET /api/receivables/month?year=&month=` → byPerson (totalDevido/jaRecebido/pendente + items) + totalPendenteGeral.
- `POST /api/receivables/{entryId}/mark` {receivedDate?} / `/unmark`.
- `POST /api/receivables/mark-batch` {entryIds,receivedDate?} → {marked}.
- `GET /api/receivables/history?personId=&fromYear=&fromMonth=&toYear=&toMonth=&status=` → totals + items.

## Históricos
- `GET /api/bills/{billId}/history?fromYear=&...` → header do molde + summary(avg/min/max/totalPaidMyShare) + items (com variation vs anterior).

## Códigos comuns
- 401 sem/invalid token · 404 recurso de outro owner · 400 validação · 409 imutabilidade/duplicado.
