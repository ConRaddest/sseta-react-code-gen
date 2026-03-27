// !!---------------------------------------------------------!!
// !!-------- AUTO-GENERATED: Edit in code generator! --------!!
// !!--------------- CHANGES HERE WILL BE LOST ---------------!!
// !!---------------------------------------------------------!!

"use client"

import { useState, useEffect } from "react"
import { ViewTemplate } from "@sseta/components"
import { useAdminStaffRoleRequest } from "@/contexts/resources/admin/AdminStaffRoleRequestContext"
import { AdminStaffRoleRequest } from "@/types/api.types"
import useAdminStaffRoleRequestViewFields from "./useAdminStaffRoleRequestViewFields"

interface AdminStaffRoleRequestViewFormProps {
  staffRoleRequestId: number
  hiddenFields?: string[]
  className?: string
  loading?: boolean
}

export default function AdminStaffRoleRequestViewForm(props: AdminStaffRoleRequestViewFormProps) {
  const { staffRoleRequestId, hiddenFields, className = "px-6 py-4", loading: loadingOverride } = props

  const [record, setRecord] = useState<AdminStaffRoleRequest | null>(null)
  const [loading, setLoading] = useState(false)
  const isLoading = loadingOverride ?? loading

  const { retrieve } = useAdminStaffRoleRequest()
  const { layout } = useAdminStaffRoleRequestViewFields()

  useEffect(() => {
    const fetchRecord = async () => {
      setLoading(true)
      try {
        const result = await retrieve(staffRoleRequestId)
        if (result) setRecord(result)
      } catch (error) {
        console.error("Failed to fetch staff role request:", error)
      } finally {
        setLoading(false)
      }
    }
    fetchRecord()
  }, [])

  return (
    <ViewTemplate
      layout={layout}
      record={record}
      isLoading={isLoading}
      hiddenFields={hiddenFields}
      className={className}
    />
  )
}
