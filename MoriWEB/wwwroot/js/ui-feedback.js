// wwwroot/js/ui-feedback.js
(function () {
    // ---- Toast ----
    function showToast(type, message, title = "") {
        const container = document.querySelector('.toast-container');
        if (!container) return;

        const el = document.createElement('div');
        const bg =
            type === 'success' ? 'text-bg-success' :
                type === 'error' ? 'text-bg-danger' :
                    type === 'warning' ? 'text-bg-warning' :
                        'text-bg-secondary';

        el.className = `toast align-items-center ${bg} border-0`;
        el.setAttribute('role', 'alert');
        el.setAttribute('aria-live', 'assertive');
        el.setAttribute('aria-atomic', 'true');

        el.innerHTML = `
      <div class="d-flex">
        <div class="toast-body">
          ${title ? `<strong class="me-2">${title}</strong>` : ""}${message || ""}
        </div>
        <button type="button" class="btn-close ${bg.includes('text-bg-warning') ? '' : 'btn-close-white'} me-2 m-auto" data-bs-dismiss="toast" aria-label="Kapat"></button>
      </div>`;

        container.appendChild(el);

        const delay =
            type === 'error' ? 4000 :
                type === 'warning' ? 3500 :
                    3000;

        const toast = new bootstrap.Toast(el, { delay });
        toast.show();
        el.addEventListener('hidden.bs.toast', () => el.remove());
    }

    // ---- Confirm (Promise) ----
    function showConfirm(message, title = 'Onay') {
        return new Promise((resolve) => {
            const modalEl = document.getElementById('confirmModal');
            const titleEl = modalEl?.querySelector('.modal-title');
            const msgEl = document.getElementById('confirmModalMessage');
            const okBtn = document.getElementById('confirmModalOk');

            if (!modalEl || !titleEl || !msgEl || !okBtn) {
                return resolve(window.confirm(message || 'Emin misiniz?'));
            }

            const bsModal = bootstrap.Modal.getOrCreateInstance(modalEl, { backdrop: 'static', keyboard: false });

            titleEl.textContent = title || 'Onay';
            msgEl.textContent = message || 'Emin misiniz?';

            const onOk = () => { cleanup(); bsModal.hide(); resolve(true); };
            const onHide = () => { cleanup(); resolve(false); };
            const cleanup = () => {
                okBtn.removeEventListener('click', onOk);
                modalEl.removeEventListener('hidden.bs.modal', onHide);
            };

            okBtn.addEventListener('click', onOk, { once: true });
            modalEl.addEventListener('hidden.bs.modal', onHide, { once: true });

            bsModal.show();
        });
    }

    // ---- İsteğe bağlı fetch yardımcıları ----
    async function apiGet(url) {
        const r = await fetch(url, { credentials: 'same-origin' });
        if (!r.ok) {
            showToast('error', `İstek başarısız (${r.status})`);
            throw new Error(`GET failed: ${r.status}`);
        }
        return r.json();
    }

    async function apiPostJson(url, data) {
        const r = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify(data ?? {})
        });

        if (r.status === 400) {
            const t = await r.text().catch(() => '');
            showToast('error', t || 'Zorunlu alanları kontrol edin.');
            return { ok: false };
        }
        if (r.status === 409) {
            const d = await safeJson(r);
            showToast('error', d?.message || 'Çakışma/tekrar kayıt.');
            return { ok: false };
        }
        if (r.status === 404) {
            showToast('error', 'Kayıt bulunamadı.');
            return { ok: false };
        }
        if (!r.ok) {
            showToast('error', 'İşlem başarısız.');
            return { ok: false };
        }

        const dataOut = await safeJson(r);
        return { ok: true, data: dataOut };
    }

    async function safeJson(resp) {
        try { return await resp.json(); } catch { return null; }
    }

    // ---- Global export ----
    window.ui = { showToast, showConfirm, apiGet, apiPostJson };
})();
