;; NETLEN - sum annotation lengths on an explicitly selected annotation layer.
;; APPLOAD this file; run NETLEN. No geometry measurements or drawing edits.
;; Groups: pipe type, open/closed installation, casing type or no casing.
;; L is route length; a 2D prefix is retained in the type, not multiplied.
;; One L per annotation; ambiguous descriptions are reported as uncounted.
(vl-load-com)

(defun nl:replace (pattern replacement value)
  (vlax-put-property nl:rx 'Pattern pattern)
  (vlax-invoke-method nl:rx 'Replace value replacement))

(defun nl:matches (pattern value / collection result item)
  (vlax-put-property nl:rx 'Pattern pattern)
  (setq collection (vlax-invoke-method nl:rx 'Execute value))
  (vlax-for item collection
    (setq result (cons (vlax-get-property item 'Value) result)))
  (vlax-release-object collection)
  (reverse result))

(defun nl:plain (s)
  ;; Preserve paragraph boundaries BEFORE stripping semicolon controls.
  (setq s (nl:replace "\\\\P|\\\\X|\\\\~" " " s)
        s (nl:replace "\\\\[fFpcCAHWTQ][^;]*;" "" s)
        s (nl:replace "\\\\[LlOoKk]|[{}]" "" s)
        s (nl:replace "%%[cC]|Ø|ø|⌀|\\\\U\\+2205|\\\\U\\+2300" "D" s))
  (vl-string-trim " \t\r\n" s))

(defun nl:visible (layer / obj)
  (setq obj (vl-catch-all-apply 'vla-Item (list nl:layers layer)))
  (and (not (vl-catch-all-error-p obj))
       (= (vla-get-LayerOn obj) :vlax-true)
       (= (vla-get-Freeze obj) :vlax-false)))

(defun nl:type-pattern ()
  "([0-9]+)?(DN[ \t]*/[ \t]*(ID|OD)|DN|ДН|D|Д|Ф)[ \t]*[0-9]+([.,][0-9]+)?([ \t]*[XХ×][ \t]*[0-9]+([.,][0-9]+)?)?([ \t]*\\+[ \t]*(D|Д|Ф)?[ \t]*[0-9]+)?")

(defun nl:casing-marker ()
  "(ФУТЛЯР[А-Я]*|Ф[ \t]*[-.][ \t]*Р[А-Я]*[.]?)")

(defun nl:type-key (s)
  (vl-string-translate "ХX×." "xxx," (nl:replace "[ \t]+" "" s)))

(defun nl:parse (s / upper lengths markers casing-pattern casings types closed opened key number)
  (setq upper (strcase s)
        ;; An explicit 'without casing' is the same as no casing description.
        upper (nl:replace (strcat "БЕЗ[ \t]+" (nl:casing-marker)) "" upper)
        lengths (nl:matches "[LЛlл][ \t]*[=:≈][ \t]*[0-9]+([.,][0-9]+)?" s)
        markers (nl:matches (nl:casing-marker) upper)
        casing-pattern (strcat (nl:casing-marker) "[ \t]*" (nl:type-pattern))
        casings (nl:matches casing-pattern upper)
        ;; Remove the casing size before finding the carrier pipe, even if
        ;; the casing description appears first in the annotation.
        types (nl:matches (nl:type-pattern) (nl:replace casing-pattern "" upper))
        closed (nl:matches "ЗАКРЫТ[А-Я]*[ \t]+(ПРОКЛАД[А-Я]*|СПОСОБ[А-Я]*)|ПРОКЛАД[А-Я]*[ \t]+ЗАКРЫТ[А-Я]*" upper)
        opened (nl:matches "ОТКРЫТ[А-Я]*[ \t]+(ПРОКЛАД[А-Я]*|СПОСОБ[А-Я]*)|ПРОКЛАД[А-Я]*[ \t]+ОТКРЫТ[А-Я]*" upper))
  (if (and (= (length lengths) 1)
           (= (length types) 1)
           (= (length markers) (length casings))
           (<= (length casings) 1)
           (not (and closed opened)))
    (progn
      (setq key (strcat (nl:type-key (car types))
                       " | " (if closed "закрытая" "открытая")
                       " | " (if casings
                         (strcat "футляр " (nl:type-key (car (nl:matches (nl:type-pattern) (car casings)))))
                         "без футляра"))
            number (atof (vl-string-translate "," "." (car (nl:matches "[0-9]+([.,][0-9]+)?" (car lengths))))))
      (list key number))))

(defun nl:consume (obj / raw s parsed key number old)
  (setq raw (vl-catch-all-apply 'vla-get-TextString (list obj)))
  (if (vl-catch-all-error-p raw)
    (setq nl:issues (cons (strcat (vla-get-Handle obj) ": нет текстового содержимого") nl:issues))
    (progn
      (setq s (nl:plain raw)
            parsed (nl:parse s))
      (cond
        (parsed
          (setq key (car parsed) number (cadr parsed) old (assoc key nl:totals))
          (if old
            (setq nl:totals (subst (cons key (+ (cdr old) number)) old nl:totals))
            (setq nl:totals (cons (cons key number) nl:totals)))
          (setq nl:count (1+ nl:count)))
        ((or (nl:matches "[LЛlл][ \t]*[=:≈]" s)
             (and (nl:matches (nl:type-pattern) (strcase s))
                  (not (nl:matches "ЗЕМ[.]|ЛОТ[.]" (strcase s)))))
          (setq nl:issues (cons (strcat (vla-get-Handle obj) ": " s) nl:issues)))))))

(defun nl:walk (space inherited path / obj kind layer name block data annotation id nl:seen)
  (vlax-for obj space
    (setq layer (vla-get-Layer obj)
          kind (vla-get-ObjectName obj))
    (if (= layer "0") (setq layer inherited))
    (if (and (member kind '("AcDbBlockReference" "AcDbMLeader" "AcDbLeader")) (or (= kind "AcDbBlockReference") (= (strcase layer) (strcase nl:target))) (nl:visible layer) (= (vla-get-Visible obj) :vlax-true))
      (cond
        ((= kind "AcDbBlockReference")
          (setq name (vla-get-Name obj))
          (if (not (member name path))
            (progn
              (setq block (vl-catch-all-apply 'vla-Item (list nl:blocks name)))
              (if (not (vl-catch-all-error-p block))
                (nl:walk block layer (cons name path))))))
        ((and (= (strcase layer) (strcase nl:target)) (= kind "AcDbMLeader"))
          (nl:consume obj))
        ((and (= (strcase layer) (strcase nl:target)) (= kind "AcDbLeader"))
          ;; Old LEADER entities refer to their MTEXT annotation via DXF 340.
          (setq data (entget (vlax-vla-object->ename obj))
                annotation (cdr (assoc 340 data)))
          (if annotation
            (progn
              (setq id (strcat (vl-princ-to-string path) "/" (cdr (assoc 5 (entget annotation)))))
              (if (not (member id nl:seen))
                (progn
                  (setq nl:seen (cons id nl:seen))
                  (nl:consume (vlax-ename->vla-object annotation)))))
            (setq nl:issues (cons (strcat (vla-get-Handle obj) ": выноска без связанного текста") nl:issues))))))))

(defun nl:format (value / s)
  (setq s (rtos value 2 3))
  (if (vl-string-search "." s)
    (progn
      (while (= (substr s (strlen s)) "0") (setq s (substr s 1 (1- (strlen s)))))
      (if (= (substr s (strlen s)) ".") (setq s (strcat s "0")))))
  (vl-string-translate "." "," s))

(defun nl:run (layer / *error* nl:rx nl:layers nl:blocks nl:target nl:totals nl:issues nl:count nl:seen doc item report total total-line)
  (defun *error* (msg)
    (if nl:rx (vlax-release-object nl:rx))
    (princ (strcat "\nNETLEN: " msg)) (princ))
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object))
        nl:layers (vla-get-Layers doc)
        nl:blocks (vla-get-Blocks doc)
        nl:target layer nl:count 0)
  (if (not (nl:visible layer))
    (princ "\nСлой не найден, выключен или заморожен.")
    (progn
      (setq nl:rx (vlax-create-object "VBScript.RegExp"))
      (vlax-put-property nl:rx 'Global :vlax-true)
      (vlax-put-property nl:rx 'IgnoreCase :vlax-false)
      (princ (strcat "\nСлой выносок: " layer))
      (nl:walk (vla-get-ModelSpace doc) "0" nil)
      (setq report "" total 0.0)
      (foreach item (vl-sort nl:totals '(lambda (a b) (< (car a) (car b))))
        (setq report (strcat report "\n" (car item) " - " (nl:format (cdr item)) "м")
              total (+ total (cdr item))))
      (princ report)
      (princ (strcat "\nУчтено выносок: " (itoa nl:count)))
      (foreach item (reverse nl:issues) (princ (strcat "\nНЕ УЧТЕНО: " item)))
      (if nl:issues (princ "\nИтог неполный: проверьте перечисленные выноски."))
      (setq total-line (strcat "\nОбщая длина " (if nl:issues "учтённых " "")
                               "участков: " (nl:format total) " м"))
      (princ total-line)
      (setq *netlen-result* (strcat report total-line))
      (vlax-release-object nl:rx) (setq nl:rx nil)))
  (princ))

(defun c:NETLEN (/ selection layer parent)
  (setq selection (nentsel "\nВыберите выноску нужной сети или Enter для ввода слоя: "))
  (if selection
    (progn
      (setq layer (cdr (assoc 8 (entget (car selection)))))
      (if (= layer "0")
        (foreach parent (cadddr selection)
          (if (= layer "0") (setq layer (cdr (assoc 8 (entget parent))))))))
    (setq layer (getstring T "\nИмя слоя выносок сети: ")))
  (if (and layer (/= layer "")) (nl:run layer))
  (princ))
(princ "\nКоманда NETLEN загружена.")
(princ)



