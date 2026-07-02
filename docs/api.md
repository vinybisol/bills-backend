# API — Endpoints

Todos os endpoints vivem sob o prefixo **`/api/v1`**. Todos exigem `Authorization: Bearer <firebase-jwt>`. `owner_id` resolvido do token. Derivados (`effective`, `myShare`, `receivable`) calculados na resposta, não persistidos.

## Infra
- `GET /api/v1/health` → resolve/provisiona o app_user e confirma liveness autenticada.

## Identidade
- `GET /api/v1/me` → perfil do app_user (id, name, email). Provisiona se novo.

## Cadastros (soft delete)
- `/api/v1/categories` — GET, POST, PUT `/{id}`, DELETE `/{id}` (desativa)
- `/api/v1/persons` — idem
- `/api/v1/incomes` — idem (molde: kind, default_amount)
- `/api/v1/bills` — idem (molde: category_id, kind, default_amount, split_ratio, person_id). Regra: split<1 exige person_id; =1 proíbe.

## Projeção
- `POST /api/v1/projection/{year}` → gera 12 entries por molde recorrente ativo. Idempotente. Resp: {billEntriesCreated, incomeEntriesCreated, skipped}.

## Lançamentos
- `GET /api/v1/entries?year=&month=` → bills[], incomes[], totals (com derivados). `totals.receivable` = a receber **pendente** (bill entries com `received=false`); `totals.received` = já recebido no mês. `received + receivable` = total a receber do mês.
- `POST /api/v1/entries/bill` {billId,year,month,plannedAmount} → 201 / 409 (duplicado).
- `POST /api/v1/entries/income` {incomeId,year,month,plannedAmount}.
- `DELETE /api/v1/entries/bill/{id}` → 204 se não pago; 409 se pago. `DELETE /api/v1/entries/income/{id}` idem (received).
- `PATCH /api/v1/entries/bill/{id}` {plannedAmount?,actualAmount?} → 200 / 409 se congelado. `PATCH /api/v1/entries/income/{id}` idem.
- `POST /api/v1/entries/bill/{id}/pay` {actualAmount?,paidDate?} → congela. `/unpay` → descongela.
- `POST /api/v1/entries/income/{id}/receive` / `/unreceive`.

## Recálculo
- `POST /api/v1/bills/{billId}/recalculate` {fromYear,fromMonth,newAmount} → atualiza default_amount + planned dos não-pagos ≥ mês. Resp: {updatedEntries, skippedPaid, newDefaultAmount}.

## Dashboards
- `GET /api/v1/dashboard/month?year=&month=` → summary + byCategory (myShare previsto/real/diff).
- `GET /api/v1/dashboard/year?year=` → months[12] + byCategory + totals.

## A receber
- `GET /api/v1/receivables/month?year=&month=` → byPerson (totalDevido/jaRecebido/pendente + items) + totalPendenteGeral.
- `POST /api/v1/receivables/{entryId}/mark` {receivedDate?} / `/unmark`.
- `POST /api/v1/receivables/mark-batch` {entryIds,receivedDate?} → {marked}.
- `GET /api/v1/receivables/history?personId=&fromYear=&fromMonth=&toYear=&toMonth=&status=` → totals + items.

## Históricos
- `GET /api/v1/bills/{billId}/history?fromYear=&...` → header do molde + summary(avg/min/max/totalPaidMyShare) + items (com variation vs anterior).

## Códigos comuns
- 401 sem/invalid token · 404 recurso de outro owner · 400 validação · 409 imutabilidade/duplicado.
