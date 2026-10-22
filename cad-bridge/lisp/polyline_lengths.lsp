;; PLLEN: label every segment of one lightweight polyline, including its closure.
;; Lengths/heights use drawing units; labels use Standard on the current layer.
;; (pllen:label entity height) uses the same implementation without UI prompts
;; and returns the created TEXT handles. Loading this file changes nothing.

(defun seglen (p1 p2 bulge / chord theta radius)
  (setq chord (distance p1 p2))
  (if (or (null bulge) (= bulge 0.0))
    chord
    (progn
      (setq theta (* 4.0 (atan (abs bulge)))
            radius (/ chord (* 2.0 (sin (/ theta 2.0)))))
      (* radius theta))))

(defun get-lwpoly-pts-bulges (edata / points bulges point bulge item)
  ;; DXF 42 follows its own vertex 10; flush that vertex at the next 10.
  (setq bulge 0.0)
  (foreach item edata
    (cond
      ((= (car item) 10)
        (if point
          (setq points (cons point points) bulges (cons bulge bulges)))
        (setq point (cdr item) bulge 0.0))
      ((= (car item) 42) (setq bulge (cdr item)))))
  (if point (setq points (cons point points) bulges (cons bulge bulges)))
  (list (reverse points) (reverse bulges)))

(defun pllen:label (entity height / edata data points bulges count closed elevation normal
                    index end-index p1 p2 length-value midpoint angle-value handles)
  (setq edata (entget entity))
  (cond
    ((/= (cdr (assoc 0 edata)) "LWPOLYLINE")
      (prompt "\nОшибка: выбран не объект LWPOLYLINE.") nil)
    ((or (not (numberp height)) (<= height 0.0))
      (prompt "\nОшибка: высота текста должна быть положительной.") nil)
    (T
      (setq data (get-lwpoly-pts-bulges edata)
            points (car data) bulges (cadr data) count (length points)
            closed (= 1 (logand 1 (cdr (assoc 70 edata))))
            elevation (cond ((cdr (assoc 38 edata))) (0.0))
            normal (cond ((cdr (assoc 210 edata))) ('(0.0 0.0 1.0)))
            end-index (if closed count (1- count)) index 0)
      ;; The closing edge uses the same writer as every other segment.
      (while (< index end-index)
        (setq p1 (nth index points) p2 (nth (rem (1+ index) count) points)
              length-value (seglen p1 p2 (nth index bulges))
              midpoint (append (mapcar '(lambda (a b) (/ (+ a b) 2.0)) p1 p2)
                               (list elevation))
              angle-value (angle p1 p2))
        (if (and (> angle-value (/ pi 2.0)) (< angle-value (* 1.5 pi)))
          (setq angle-value (+ angle-value pi)))
        (if (not (entmake
          (list '(0 . "TEXT") (cons 10 midpoint) (cons 40 height)
                (cons 1 (rtos length-value 2 1)) (cons 50 angle-value)
                '(7 . "Standard") '(72 . 1) '(73 . 2)
                (cons 11 midpoint) (cons 210 normal))))
          (progn (prompt "\nНе удалось создать подпись; проверь слой и созданные объекты.") (exit)))
        (setq handles (cons (cdr (assoc 5 (entget (entlast)))) handles)
              index (1+ index)))
      (reverse handles))))

(defun c:PLLEN (/ picked entity height handles)
  (setq picked (entsel "\nВыбери полилинию: "))
  (if picked
    (progn
      (setq entity (car picked))
      (if (= (cdr (assoc 0 (entget entity))) "LWPOLYLINE")
        (progn
          (initget 6)
          (setq height (getreal "\nВысота текста <2.5>: "))
          (setq handles (pllen:label entity (if height height 2.5)))
          (if handles (prompt (strcat "\nГотово. Подписано участков: " (itoa (length handles))))))
        (prompt "\nОшибка: выбран не объект LWPOLYLINE."))))
  (princ))
