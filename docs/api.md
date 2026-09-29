# API — Endpoints

Todos os endpoints vivem sob o prefixo **`/api/v1`**. Todos exigem `Authorization: Bearer <firebase-jwt>`. `owner_id` resolvido do token. Derivados (`effective`, `myShare`, `receivable`) calculados na resposta, não persistidos.

## Infra
- `GET /api/v1/health` → resolve/provisiona o app_user e confirma liveness autenticada.

## Identidade
- `GET /api/v1/me` → perfil do app_user (id, name, email). Provisiona se novo.

## Cadastros (soft delete)
- `/api/v1/categories` — GET, POST, PUT `/{id}`, DELETE `/{id}` (desativa)
- `/api/v1/persons` — idem
- `/api/v1/incomes` — idem (molde: kind, default_amount). Contrato padronizado (Result → HTTP):
  - `POST` {name, kind: `recurring`|`one_off`, defaultAmount ≥ 0} → 201 + `Location: /api/v1/incomes/{id}` + {id,name,kind,defaultAmount}.
  - `GET` → 200 com a lista (ordenada por nome, só ativos) ou **204 se vazia**.
  - `PUT /{id}` {name,kind,defaultAmount} → 200 com o DTO atualizado; 404 (ProblemDetails) se não existe/inativo/de outro owner.
  - `DELETE /{id}` → 204 (soft delete, `active=false`); 404 (ProblemDetails) se não existe/já inativo/de outro owner.
  - Validação → 400 `application/problem+json` (ValidationProblem) com `errors` por campo: `name` (vazio), `kind` (valor fora do enum), `defaultAmount` (negativo). `kind` string desconhecido → 400 no binding.
- `/api/v1/bills` — molde: categoryId, kind, defaultAmount, splitRatio, personId. Contrato padronizado (Result → HTTP):
  - `POST` {name, categoryId, kind: `recurring`|`one_off`, defaultAmount ≥ 0, splitRatio ∈ [0,1], personId?} → 201 + `Location: /api/v1/bills/{id}` + {id,name,categoryId,kind,defaultAmount,splitRatio,personId}.
  - `GET` → 200 com a lista (ordenada por nome, só ativos) ou **204 se vazia**.
  - `PUT /{id}` (mesmo corpo) → 200 com o DTO atualizado; 404 (ProblemDetails) se o molde não existe/inativo/de outro owner.
  - `DELETE /{id}` → 204 (soft delete, `active=false`); 404 (ProblemDetails) se não existe/já inativo/de outro owner.
  - Validação → 400 ValidationProblem com `errors` por campo: `name` (vazio), `kind` (fora do enum), `defaultAmount` (negativo), `splitRatio` (fora de [0,1]), `personId` (split<1 exige; =1 proíbe). Validação roda antes de qualquer acesso ao banco.
  - `categoryId`/`personId` inexistente, inativo ou de outro owner → 404 (ProblemDetails) em POST/PUT.

## Projeção
- `POST /api/v1/projection/{year}` → gera 12 entries por molde **recorrente ativo** (bills e incomes; `one_off` e moldes desativados são ignorados). Cada entry leva **snapshot** de `plannedAmount` (e, em bills, `splitRatioSnapshot`/`personId`) — nunca referência ao molde. Idempotente: meses que já têm entry (por molde + ano + mês) são pulados; entries existentes (inclusive pagas) nunca são alteradas. Contrato padronizado (Result → HTTP):
  - 200 {year, billEntriesCreated, incomeEntriesCreated, skipped} (também quando nada é criado).
  - 400 ValidationProblem com `errors.year` se `year` fora de **2000–2100**; nada é persistido. `year` não numérico → 404 (constraint de rota `{year:int}`).
  - 409 ProblemDetails se uma projeção/lançamento concorrente inserir o mesmo mês entre a leitura e a gravação (nada é persistido; repetir a chamada é seguro).
  - 401 sem token válido.

## Lançamentos
- `GET /api/v1/entries?year=&month=` → bills[], incomes[], totals (com derivados). `totals.receivable` = a receber **pendente** (bill entries com `received=false`, alias de `receivablePending`); `totals.received` = já recebido no mês (alias de `receivableReceived`). `received + receivable` = total a receber do mês. `totals.paidFull` = valor cheio (não myShare) dos bill entries pagos. Três saldos: `saldoPrevistoOtimista` (= `saldoPrevisto`, assume que todo pendente será recebido) = incomesPlanned − mySharePlanned; `saldoPrevistoPiorCaso` = saldoPrevistoOtimista − receivablePending (assume que o pendente nunca será pago); `saldoRealizado` (= `saldoReal`) = (incomesReceived + receivableReceived) − paidFull. Nota: `saldoReal` mudou de semântica — antes era baseado no myShare dos bills pagos, agora usa o valor cheio pago (`paidFull`).
- `POST /api/v1/entries/bill` {billId,year,month,plannedAmount} → 201 / 409 (duplicado).
- `POST /api/v1/entries/income` {incomeId,year,month,plannedAmount}.
- `DELETE /api/v1/entries/bill/{id}` → 204 se não pago; 409 se pago. `DELETE /api/v1/entries/income/{id}` idem (received).
- `PATCH /api/v1/entries/bill/{id}` {plannedAmount?,actualAmount?} → 200 / 409 se congelado. `PATCH /api/v1/entries/income/{id}` idem.
- `POST /api/v1/entries/bill/{id}/pay` {actualAmount?,paidDate?} → congela. `/unpay` → descongela.
- `POST /api/v1/entries/income/{id}/receive` / `/unreceive`.

## Recálculo
- `POST /api/v1/bills/{billId}/recalculate` {fromYear,fromMonth,newAmount} → atualiza default_amount + planned dos não-pagos ≥ mês (pagos ficam congelados). 200 {billId, updatedEntries, skippedPaid, newDefaultAmount}. 400 ValidationProblem (`fromMonth` fora de 1–12, `newAmount` negativo); 404 ProblemDetails se o molde não existe/inativo/de outro owner.

## Dashboards
- `GET /api/v1/dashboard/month?year=&month=` → summary + byCategory (myShare previsto/real/diff). `summary` inclui `receivablePending`, `receivableReceived`, `paidFull` (valor cheio dos bill entries pagos) e os três saldos `saldoPrevistoOtimista`/`saldoPrevistoPiorCaso`/`saldoRealizado` — mesma semântica de `GET /api/v1/entries` (ver acima), incluindo a mudança de `saldoReal` para usar `paidFull` em vez do myShare dos bills pagos.
- `GET /api/v1/dashboard/year?year=` → months[12] + byCategory + totals.

## A receber
- `GET /api/v1/receivables/month?year=&month=` → byPerson (totalDevido/jaRecebido/pendente + items) + totalPendenteGeral.
- `POST /api/v1/receivables/{entryId}/mark` {receivedDate?} / `/unmark`.
- `POST /api/v1/receivables/mark-batch` {entryIds,receivedDate?} → {marked}.
- `GET /api/v1/receivables/history?personId=&fromYear=&fromMonth=&toYear=&toMonth=&status=` → totals + items.

## Históricos
- `GET /api/v1/bills/{billId}/history?fromYear=&fromMonth=&toYear=&toMonth=` → 200 header do molde {billId,name,category,splitRatio,person} + summary(avgEffective/minEffective/maxEffective/totalPaidMyShare) + items (com variation vs anterior). Resolve também moldes desativados. 404 ProblemDetails se não existe/de outro owner.

## Códigos comuns
- 401 sem/invalid token · 404 recurso de outro owner · 400 validação · 409 imutabilidade/duplicado.
