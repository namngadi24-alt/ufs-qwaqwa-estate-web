/* Card entry: live card preview, client-side validation, and a confirmation card shown after pressing Pay. */
(function () {
    function digits(s) { return (s || '').replace(/\D/g, ''); }
    // Card type comes from the LENGTH of the number: 15 = Amex, 16 = Visa (starts with 4) or Mastercard, 13/19 = Visa.
    function brand(n) {
        if (n.length === 15) return 'AMEX';
        if (n.length === 16) return n.charAt(0) === '4' ? 'VISA' : 'MASTERCARD';
        if (n.length === 13 || n.length === 19) return 'VISA';
        return '';
    }
    function pretty(n) { return n.replace(/(.{4})/g, '$1 ').trim(); }
    function masked(n) { if (!n) return '\u2022\u2022\u2022\u2022 \u2022\u2022\u2022\u2022 \u2022\u2022\u2022\u2022 \u2022\u2022\u2022\u2022'; var m = n.length > 4 ? '\u2022'.repeat(n.length - 4) + n.slice(-4) : n; return pretty(m.padEnd(16, '\u2022').slice(0, Math.max(m.length, 16))); }

    window.initCardForm = function (getAmount, validateExtra) {
        var holder = document.getElementById('CardHolder'), number = document.getElementById('CardNumber'),
            expiry = document.getElementById('Expiry'), cvv = document.getElementById('Cvv'),
            form = document.getElementById('payForm'), errBox = document.getElementById('clientErrors');
        if (!form) return;
        function preview() {
            var n = digits(number.value);
            document.getElementById('pvNumber').textContent = masked(n);
            document.getElementById('pvHolder').textContent = (holder.value || 'NAME ON CARD').toUpperCase();
            document.getElementById('pvExpiry').textContent = expiry.value || 'MM/YY';
            document.getElementById('pvBrand').textContent = brand(n);
        }
        [holder, number, expiry, cvv].forEach(function (el) { el.addEventListener('input', preview); });
        number.addEventListener('input', function () { var n = digits(number.value).slice(0, 19); number.value = pretty(n); });
        expiry.addEventListener('input', function () { var v = expiry.value.replace(/[^\d]/g, '').slice(0, 4); if (v.length > 2) v = v.slice(0, 2) + '/' + v.slice(2); expiry.value = v; });
        preview();

        function validate() {
            var errors = [], n = digits(number.value);
            if (validateExtra) { var e = validateExtra(); if (e) errors.push(e); }
            if (!/^[A-Za-z][A-Za-z '\-.]{1,59}$/.test(holder.value.trim())) errors.push('Enter the name on the card (letters only).');
            if (n.length < 13 || n.length > 19) errors.push('The card number must be between 13 and 19 digits.');
            else if (!brand(n)) errors.push('The card number must have 13, 15, 16 or 19 digits (15 = American Express, 16 = Visa or Mastercard, 13/19 = Visa).');
            var m = /^(0[1-9]|1[0-2])\/(\d{2})$/.exec(expiry.value.trim());
            if (!m) errors.push('Enter the expiry date as MM/YY.');
            else { var end = new Date(2000 + (+m[2]), +m[1], 0, 23, 59, 59); if (end < new Date()) errors.push('This card has expired.'); }
            var need = brand(n) === 'AMEX' ? 4 : 3;
            if (!/^\d+$/.test(cvv.value) || cvv.value.length !== need) errors.push('The security code must be ' + need + ' digits.');
            return errors;
        }

        var modalEl = document.getElementById('confirmModal'), modal = modalEl ? new bootstrap.Modal(modalEl) : null;
        document.getElementById('payBtn').addEventListener('click', function () {
            var errors = validate();
            if (errors.length) {
                errBox.innerHTML = '<ul class="mb-0">' + errors.map(function (e) { return '<li>' + e + '</li>'; }).join('') + '</ul>';
                errBox.classList.remove('d-none'); errBox.scrollIntoView({ behavior: 'smooth', block: 'center' }); return;
            }
            errBox.classList.add('d-none');
            document.getElementById('cmNumber').textContent = masked(digits(number.value));
            document.getElementById('cmHolder').textContent = holder.value.toUpperCase();
            document.getElementById('cmExpiry').textContent = expiry.value;
            document.getElementById('cmBrand').textContent = brand(digits(number.value));
            document.getElementById('cmAmount').textContent = 'R' + getAmount().toFixed(2);
            modal.show();
        });
        document.getElementById('confirmBtn').addEventListener('click', function () { this.disabled = true; this.textContent = 'Processing...'; form.submit(); });
    };
})();
