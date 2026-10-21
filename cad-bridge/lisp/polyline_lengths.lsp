(defun seglen (p1 p2 bulge / chord theta radius)
  (setq chord (distance p1 p2))
  (if (or (null bulge) (= bulge 0.0))
    chord
    (progn
      (setq theta (* 4.0 (atan (abs bulge))))
      (setq radius (/ chord (* 2.0 (sin (/ theta 2.0)))))
      (* radius theta)
    )
  )
)

(defun get-lwpoly-pts-bulges (edata / pts bulges item lastbulge)
  (setq pts '())
  (setq bulges '())
  (setq lastbulge 0.0)

  (foreach item edata
    (cond
      ((= (car item) 42)
       (setq lastbulge (cdr item))
      )
      ((= (car item) 10)
       (setq pts (append pts (list (cdr item))))
       (setq bulges (append bulges (list lastbulge)))
       (setq lastbulge 0.0)
      )
    )
  )
  (list pts bulges)
)

(defun c:pllen (/ ent edata data pts bulges closed h i p1 p2 b len mid ang txt)
  (setq ent (car (entsel "\nВыбери полилинию: ")))

  (if ent
    (progn
      (setq edata (entget ent))

      (if (= (cdr (assoc 0 edata)) "LWPOLYLINE")
        (progn
          (setq data (get-lwpoly-pts-bulges edata))
          (setq pts (car data))
          (setq bulges (cadr data))
          (setq closed (= 1 (logand 1 (cdr (assoc 70 edata)))))

          (setq h (getreal "\nВысота текста <2.5>: "))
          (if (null h) (setq h 2.5))

          (setq i 0)

          (while (< i (- (length pts) 1))
            (setq p1 (nth i pts))
            (setq p2 (nth (+ i 1) pts))
            (setq b (nth i bulges))
            (setq len (seglen p1 p2 b))
            (setq mid (mapcar '(lambda (a b) (/ (+ a b) 2.0)) p1 p2))
            (setq ang (angle p1 p2))
		(if (and (> ang (/ pi 2.0)) (< ang (* 1.5 pi)))
  		(setq ang (+ ang pi))
		)
            (setq txt (rtos len 2 1))

            (entmake
              (list
                '(0 . "TEXT")
                (cons 10 mid)
                (cons 40 h)
                (cons 1 txt)
                (cons 50 ang)
                '(7 . "Standard")
                '(72 . 1)
                '(73 . 2)
                (cons 11 mid)
              )
            )

            (setq i (+ i 1))
          )

          (if closed
            (progn
              (setq p1 (last pts))
              (setq p1 (car p1))
              (setq p2 (car pts))
              (setq b (nth (- (length bulges) 1) bulges))
              (setq len (seglen p1 p2 b))
              (setq mid (mapcar '(lambda (a b) (/ (+ a b) 2.0)) p1 p2))
              (setq ang (angle p1 p2))
		(if (and (> ang (/ pi 2.0)) (< ang (* 1.5 pi)))
  		(setq ang (+ ang pi))
		)
              (setq txt (rtos len 2 1))

              (entmake
                (list
                  '(0 . "TEXT")
                  (cons 10 mid)
                  (cons 40 h)
                  (cons 1 txt)
                  (cons 50 ang)
                  '(7 . "Standard")
                  '(72 . 1)
                  '(73 . 2)
                  (cons 11 mid)
                )
              )
            )
          )

          (princ "\nГотово. Длины участков подписаны.")
        )
        (princ "\nОшибка: выбран не объект LWPOLYLINE.")
      )
    )
  )

  (princ)
)